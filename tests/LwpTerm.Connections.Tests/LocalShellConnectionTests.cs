using System;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using LwpTerm.Connections.Local;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LwpTerm.Connections.Tests;

[Trait("Category", "Integration")]
public class LocalShellConnectionTests
{
    [Fact]
    public async Task Cmd_echo_is_received_through_ConPTY()
    {
        var settings = new LocalShellConnectionSettings
        {
            ShellKind = LocalShellKind.Cmd,
            Arguments = "/c echo LWPTERM_CONPTY_OK & exit"
        };

        await using var conn = new LocalShellConnection(settings, NullLogger.Instance);

        var output = new StringBuilder();
        var closed = new TaskCompletionSource();
        conn.DataReceived += (_, mem) => output.Append(Encoding.UTF8.GetString(mem.Span));
        conn.Closed += (_, _) => closed.TrySetResult();

        await conn.ConnectAsync(120, 30);

        await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromSeconds(10)));

        output.ToString().Should().Contain("LWPTERM_CONPTY_OK");
    }

    [Fact]
    public async Task Input_is_echoed_back_by_an_interactive_cmd()
    {
        var settings = new LocalShellConnectionSettings { ShellKind = LocalShellKind.Cmd };
        await using var conn = new LocalShellConnection(settings, NullLogger.Instance);

        var output = new StringBuilder();
        conn.DataReceived += (_, mem) =>
        {
            lock (output)
            {
                output.Append(Encoding.UTF8.GetString(mem.Span));
            }
        };

        await conn.ConnectAsync(120, 30);
        await Task.Delay(500);
        await conn.WriteAsync(Encoding.UTF8.GetBytes("echo ROUNDTRIP_MARKER\r\n"));

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (output)
            {
                if (output.ToString().Contains("ROUNDTRIP_MARKER"))
                {
                    return;
                }
            }

            await Task.Delay(100);
        }

        lock (output)
        {
            output.ToString().Should().Contain("ROUNDTRIP_MARKER");
        }
    }

    [Fact]
    public async Task Resize_before_and_after_connect_does_not_throw()
    {
        var settings = new LocalShellConnectionSettings { ShellKind = LocalShellKind.Cmd };
        await using var conn = new LocalShellConnection(settings, NullLogger.Instance);

        await conn.ResizeAsync(80, 24); // no-op before connect
        await conn.ConnectAsync(80, 24);
        await conn.ResizeAsync(132, 43);
        await conn.ResizeAsync(100, 30);
    }
}
