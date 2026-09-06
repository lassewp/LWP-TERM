using System;
using System.Collections.Generic;
using System.Text;

namespace LwpTerm.Connections.Telnet;

/// <summary>
/// A minimal Telnet (RFC 854) NVT byte filter: strips and answers IAC command
/// sequences, passes everything else through as terminal data. Handles just the
/// options an interactive terminal needs — SGA, ECHO, TERMINAL-TYPE and NAWS —
/// and refuses the rest. Stateful across reads (IAC sequences may be split).
/// </summary>
public sealed class TelnetOptionNegotiator
{
    // Commands
    private const byte IAC = 255;
    private const byte SE = 240;
    private const byte SB = 250;
    private const byte WILL = 251;
    private const byte WONT = 252;
    private const byte DO = 253;
    private const byte DONT = 254;

    // Options
    private const byte OPT_ECHO = 1;
    private const byte OPT_SGA = 3;
    private const byte OPT_TTYPE = 24;
    private const byte OPT_NAWS = 31;

    // Sub-negotiation
    private const byte TTYPE_IS = 0;
    private const byte TTYPE_SEND = 1;

    private enum State
    {
        Data,
        Iac,
        Option,     // expecting option byte after WILL/WONT/DO/DONT
        SubOption,  // collecting SB payload until IAC SE
        SubIac      // saw IAC inside SB
    }

    private State _state = State.Data;
    private byte _pendingCommand;
    private readonly List<byte> _subBuffer = new();

    private string _terminalType = "xterm-256color";
    private int _cols = 80;
    private int _rows = 24;

    /// <summary>Proactive negotiations to send immediately after the socket connects.</summary>
    public byte[] InitialHandshake() => new byte[]
    {
        IAC, WILL, OPT_TTYPE,
        IAC, WILL, OPT_NAWS,
        IAC, DO, OPT_SGA,
        IAC, DO, OPT_ECHO
    };

    public void SetTerminalType(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _terminalType = value.Trim();
        }
    }

    /// <summary>Record a new window size; returns the NAWS sub-negotiation to send (empty until first set).</summary>
    public byte[] SetWindowSize(int cols, int rows)
    {
        _cols = Math.Clamp(cols, 1, 65535);
        _rows = Math.Clamp(rows, 1, 65535);
        return BuildNaws();
    }

    /// <summary>
    /// Consume raw bytes from the socket. Fills <paramref name="applicationData"/>
    /// with terminal bytes and <paramref name="response"/> with any IAC replies to
    /// write back.
    /// </summary>
    public void Process(ReadOnlySpan<byte> input, out byte[] applicationData, out byte[] response)
    {
        var app = new List<byte>(input.Length);
        var reply = new List<byte>();

        foreach (var b in input)
        {
            switch (_state)
            {
                case State.Data:
                    if (b == IAC)
                    {
                        _state = State.Iac;
                    }
                    else
                    {
                        app.Add(b);
                    }

                    break;

                case State.Iac:
                    if (b == IAC)
                    {
                        app.Add(IAC); // escaped 0xFF literal
                        _state = State.Data;
                    }
                    else if (b is WILL or WONT or DO or DONT)
                    {
                        _pendingCommand = b;
                        _state = State.Option;
                    }
                    else if (b == SB)
                    {
                        _subBuffer.Clear();
                        _state = State.SubOption;
                    }
                    else
                    {
                        // Standalone command (GA, NOP, ...) — ignore.
                        _state = State.Data;
                    }

                    break;

                case State.Option:
                    HandleOption(_pendingCommand, b, reply);
                    _state = State.Data;
                    break;

                case State.SubOption:
                    if (b == IAC)
                    {
                        _state = State.SubIac;
                    }
                    else
                    {
                        _subBuffer.Add(b);
                    }

                    break;

                case State.SubIac:
                    if (b == SE)
                    {
                        HandleSubNegotiation(_subBuffer, reply);
                        _subBuffer.Clear();
                        _state = State.Data;
                    }
                    else if (b == IAC)
                    {
                        _subBuffer.Add(IAC);
                        _state = State.SubOption;
                    }
                    else
                    {
                        _state = State.SubOption;
                    }

                    break;
            }
        }

        applicationData = app.ToArray();
        response = reply.ToArray();
    }

    private void HandleOption(byte command, byte option, List<byte> reply)
    {
        switch (command)
        {
            case DO:
                if (option is OPT_TTYPE or OPT_NAWS or OPT_SGA)
                {
                    reply.AddRange(new[] { IAC, WILL, option });
                    if (option == OPT_NAWS)
                    {
                        reply.AddRange(BuildNaws());
                    }
                }
                else
                {
                    reply.AddRange(new[] { IAC, WONT, option });
                }

                break;

            case DONT:
                reply.AddRange(new[] { IAC, WONT, option });
                break;

            case WILL:
                if (option is OPT_ECHO or OPT_SGA)
                {
                    reply.AddRange(new[] { IAC, DO, option });
                }
                else
                {
                    reply.AddRange(new[] { IAC, DONT, option });
                }

                break;

            case WONT:
                reply.AddRange(new[] { IAC, DONT, option });
                break;
        }
    }

    private void HandleSubNegotiation(List<byte> payload, List<byte> reply)
    {
        if (payload.Count >= 2 && payload[0] == OPT_TTYPE && payload[1] == TTYPE_SEND)
        {
            reply.Add(IAC);
            reply.Add(SB);
            reply.Add(OPT_TTYPE);
            reply.Add(TTYPE_IS);
            foreach (var ch in Encoding.ASCII.GetBytes(_terminalType))
            {
                reply.Add(ch);
            }

            reply.Add(IAC);
            reply.Add(SE);
        }
    }

    private byte[] BuildNaws()
    {
        Span<byte> size = stackalloc byte[]
        {
            (byte)(_cols >> 8), (byte)(_cols & 0xFF),
            (byte)(_rows >> 8), (byte)(_rows & 0xFF)
        };

        var result = new List<byte>(12) { IAC, SB, OPT_NAWS };
        foreach (var b in size)
        {
            result.Add(b);
            if (b == IAC)
            {
                result.Add(IAC); // double 0xFF inside sub-negotiation
            }
        }

        result.Add(IAC);
        result.Add(SE);
        return result.ToArray();
    }
}
