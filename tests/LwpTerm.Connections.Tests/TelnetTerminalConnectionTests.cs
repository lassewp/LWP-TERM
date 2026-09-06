using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using LwpTerm.Connections.Telnet;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LwpTerm.Connections.Tests;

public class TelnetTerminalConnectionTests
{
    [Fact]
    public async Task Round_trips_data_against_a_loopback_echo_server_and_answers_IAC()
    {
        using var server = new LoopbackTelnetServer();
        server.Start();

        var settings = new TelnetConnectionSettings { Host = "127.0.0.1", Port = server.Port };
        await using var conn = new TelnetTerminalConnection(settings, NullLogger.Instance);

        var received = new StringBuilder();
        conn.DataReceived += (_, m) =>
        {
            lock (received) { received.Append(Encoding.ASCII.GetString(m.Span)); }
        };

        await conn.ConnectAsync(100, 30);

        // Server offers DO NAWS; the client should have replied WILL NAWS + size.
        await server.WaitForClientHandshakeAsync(TimeSpan.FromSeconds(5));
        server.ClientSaidWillNaws.Should().BeTrue();

        await conn.WriteAsync(Encoding.ASCII.GetBytes("ping\r\n"));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (received)
            {
                if (received.ToString().Contains("ping"))
                {
                    return;
                }
            }

            await Task.Delay(50);
        }

        lock (received)
        {
            received.ToString().Should().Contain("ping");
        }
    }

    /// <summary>Accepts one connection, sends <c>IAC DO NAWS</c>, then echoes everything (IAC stripped).</summary>
    private sealed class LoopbackTelnetServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly TaskCompletionSource _handshake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenSource? _cts;

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public bool ClientSaidWillNaws { get; private set; }

        public void Start()
        {
            _listener.Start();
            _cts = new CancellationTokenSource();
            _ = AcceptAsync(_cts.Token);
        }

        public Task WaitForClientHandshakeAsync(TimeSpan timeout) =>
            Task.WhenAny(_handshake.Task, Task.Delay(timeout));

        private async Task AcceptAsync(CancellationToken ct)
        {
            try
            {
                using var client = await _listener.AcceptTcpClientAsync(ct);
                using var stream = client.GetStream();

                await stream.WriteAsync(new byte[] { 255, 253, 31 }, ct); // IAC DO NAWS

                var buffer = new byte[1024];
                while (!ct.IsCancellationRequested)
                {
                    var read = await stream.ReadAsync(buffer, ct);
                    if (read <= 0)
                    {
                        break;
                    }

                    var echo = new System.Collections.Generic.List<byte>(read);
                    for (var i = 0; i < read; i++)
                    {
                        if (buffer[i] == 255 && i + 2 < read)
                        {
                            if (buffer[i + 1] == 251 && buffer[i + 2] == 31)
                            {
                                ClientSaidWillNaws = true;
                            }

                            i += 2; // skip the 3-byte IAC command
                            _handshake.TrySetResult();
                            continue;
                        }

                        echo.Add(buffer[i]);
                    }

                    if (echo.Count > 0)
                    {
                        await stream.WriteAsync(echo.ToArray(), ct);
                    }
                }
            }
            catch
            {
                // listener stopped / client gone
            }
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _listener.Stop();
        }
    }
}
