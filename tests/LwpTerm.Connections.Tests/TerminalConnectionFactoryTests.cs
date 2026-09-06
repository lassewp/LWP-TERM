using System;
using System.Threading.Tasks;
using FluentAssertions;
using LwpTerm.Connections;
using LwpTerm.Connections.Local;
using LwpTerm.Connections.Serial;
using LwpTerm.Connections.Ssh;
using LwpTerm.Connections.Telnet;
using LwpTerm.Core;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LwpTerm.Connections.Tests;

public class TerminalConnectionFactoryTests : IDisposable
{
    private readonly string _dir;
    private readonly TerminalConnectionFactory _factory;

    public TerminalConnectionFactoryTests()
    {
        _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lwpterm-fac-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_dir);
        var paths = AppPaths.Portable(_dir);
        _factory = new TerminalConnectionFactory(
            NullLoggerFactory.Instance,
            new CredentialStore(paths, NullLogger<CredentialStore>.Instance),
            new TrustAllHostKeyVerifier(),
            new KnownHostsStore(paths));
    }

    public void Dispose() => System.IO.Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(ProtocolType.LocalShell, true)]
    [InlineData(ProtocolType.Ssh, true)]
    [InlineData(ProtocolType.Telnet, true)]
    [InlineData(ProtocolType.Serial, true)]
    [InlineData(ProtocolType.Sftp, false)]
    [InlineData(ProtocolType.Ftp, false)]
    [InlineData(ProtocolType.Rdp, false)]
    [InlineData(ProtocolType.Vnc, false)]
    public void Supports_reports_the_terminal_protocols(ProtocolType protocol, bool expected)
        => _factory.Supports(protocol).Should().Be(expected);

    [Fact]
    public void Create_returns_the_matching_transport_type()
    {
        _factory.Create(new LocalShellConnectionSettings()).Should().BeOfType<LocalShellConnection>();
        _factory.Create(new SshConnectionSettings()).Should().BeOfType<SshTerminalConnection>();
        _factory.Create(new TelnetConnectionSettings()).Should().BeOfType<TelnetTerminalConnection>();
        _factory.Create(new SerialConnectionSettings()).Should().BeOfType<SerialTerminalConnection>();
    }

    [Fact]
    public void Create_rejects_a_protocol_without_a_terminal_transport()
    {
        Assert.Throws<NotSupportedException>(() => _factory.Create(new RdpConnectionSettings()));
    }

    [Fact]
    public async Task Ssh_connect_to_a_closed_port_fails_without_hanging()
    {
        var settings = new SshConnectionSettings { Host = "127.0.0.1", Port = 59_999, Username = "nobody" };
        await using var conn = _factory.Create(settings);

        var connect = conn.ConnectAsync(80, 24);
        var finished = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(25)));

        finished.Should().BeSameAs(connect, "connect should fail fast, not hang");
        await Assert.ThrowsAnyAsync<Exception>(() => connect);
        conn.IsConnected.Should().BeFalse();
    }
}
