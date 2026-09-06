using System;
using System.Linq;
using FluentAssertions;
using LwpTerm.Connections.Telnet;

namespace LwpTerm.Connections.Tests;

public class TelnetOptionNegotiatorTests
{
    private const byte IAC = 255;
    private const byte SE = 240;
    private const byte SB = 250;
    private const byte WILL = 251;
    private const byte WONT = 252;
    private const byte DO = 253;
    private const byte DONT = 254;
    private const byte ECHO = 1;
    private const byte SGA = 3;
    private const byte TTYPE = 24;
    private const byte NAWS = 31;

    [Fact]
    public void Plain_data_passes_through_untouched()
    {
        var n = new TelnetOptionNegotiator();
        n.Process("hello world"u8, out var app, out var reply);

        System.Text.Encoding.ASCII.GetString(app).Should().Be("hello world");
        reply.Should().BeEmpty();
    }

    [Fact]
    public void Escaped_FF_byte_is_unescaped_into_data()
    {
        var n = new TelnetOptionNegotiator();
        n.Process(new byte[] { (byte)'a', IAC, IAC, (byte)'b' }, out var app, out _);

        app.Should().Equal((byte)'a', 255, (byte)'b');
    }

    [Fact]
    public void DO_SGA_is_accepted_with_WILL_SGA()
    {
        var n = new TelnetOptionNegotiator();
        n.Process(new byte[] { IAC, DO, SGA }, out var app, out var reply);

        app.Should().BeEmpty();
        reply.Should().Equal(IAC, WILL, SGA);
    }

    [Fact]
    public void DO_an_unsupported_option_is_refused_with_WONT()
    {
        var n = new TelnetOptionNegotiator();
        n.Process(new byte[] { IAC, DO, 99 }, out _, out var reply);

        reply.Should().Equal(IAC, WONT, 99);
    }

    [Fact]
    public void WILL_ECHO_is_answered_with_DO_ECHO()
    {
        var n = new TelnetOptionNegotiator();
        n.Process(new byte[] { IAC, WILL, ECHO }, out _, out var reply);

        reply.Should().Equal(IAC, DO, ECHO);
    }

    [Fact]
    public void DO_NAWS_replies_WILL_NAWS_then_the_window_size()
    {
        var n = new TelnetOptionNegotiator();
        n.SetWindowSize(120, 40);

        n.Process(new byte[] { IAC, DO, NAWS }, out _, out var reply);

        // IAC WILL NAWS, then IAC SB NAWS 00 78 00 28 IAC SE
        reply.Should().Equal(
            IAC, WILL, NAWS,
            IAC, SB, NAWS, 0x00, 0x78, 0x00, 0x28, IAC, SE);
    }

    [Fact]
    public void TerminalType_subnegotiation_send_returns_the_configured_name()
    {
        var n = new TelnetOptionNegotiator();
        n.SetTerminalType("xterm-256color");

        // IAC SB TTYPE SEND IAC SE
        n.Process(new byte[] { IAC, SB, TTYPE, 1, IAC, SE }, out var app, out var reply);

        app.Should().BeEmpty();
        var text = System.Text.Encoding.ASCII.GetString(reply.Skip(4).Take(reply.Length - 6).ToArray());
        reply.Take(4).Should().Equal(IAC, SB, TTYPE, 0 /* IS */);
        text.Should().Be("xterm-256color");
        reply.TakeLast(2).Should().Equal(IAC, SE);
    }

    [Fact]
    public void IAC_sequence_split_across_two_reads_is_handled()
    {
        var n = new TelnetOptionNegotiator();

        n.Process(new byte[] { (byte)'x', IAC }, out var app1, out var reply1);
        n.Process(new byte[] { DO, SGA, (byte)'y' }, out var app2, out var reply2);

        System.Text.Encoding.ASCII.GetString(app1).Should().Be("x");
        reply1.Should().BeEmpty();
        System.Text.Encoding.ASCII.GetString(app2).Should().Be("y");
        reply2.Should().Equal(IAC, WILL, SGA);
    }

    [Fact]
    public void NAWS_payload_doubles_a_255_dimension_byte()
    {
        var n = new TelnetOptionNegotiator();
        var naws = n.SetWindowSize(255, 24); // 255 -> 0x00 0xFF, and 0xFF must be doubled

        naws.Should().Equal(IAC, SB, NAWS, 0x00, 0xFF, 0xFF, 0x00, 0x18, IAC, SE);
    }
}
