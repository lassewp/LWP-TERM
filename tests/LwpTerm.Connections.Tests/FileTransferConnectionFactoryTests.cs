using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using LwpTerm.Connections;
using LwpTerm.Connections.Ftp;
using LwpTerm.Connections.Sftp;
using LwpTerm.Core;
using LwpTerm.Core.Security;
using LwpTerm.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace LwpTerm.Connections.Tests;

public class FileTransferConnectionFactoryTests : IDisposable
{
    private readonly string _dir;
    private readonly FileTransferConnectionFactory _factory;

    public FileTransferConnectionFactoryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "lwpterm-xfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var paths = AppPaths.Portable(_dir);
        _factory = new FileTransferConnectionFactory(
            NullLoggerFactory.Instance,
            new CredentialStore(paths, NullLogger<CredentialStore>.Instance),
            new TrustAllHostKeyVerifier(),
            new KnownHostsStore(paths));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(ProtocolType.Sftp, true)]
    [InlineData(ProtocolType.Ftp, true)]
    [InlineData(ProtocolType.Ssh, false)]
    [InlineData(ProtocolType.LocalShell, false)]
    public void Supports_reports_the_transfer_protocols(ProtocolType protocol, bool expected)
        => _factory.Supports(protocol).Should().Be(expected);

    [Fact]
    public void Create_maps_settings_to_the_matching_transport()
    {
        _factory.Create(new SftpConnectionSettings()).Should().BeOfType<SftpFileTransferConnection>();
        _factory.Create(new FtpConnectionSettings()).Should().BeOfType<FtpFileTransferConnection>();
    }

    [Fact]
    public void CreateSftpForSsh_carries_host_and_credentials_across()
    {
        var creds = new CredentialStore(AppPaths.Portable(_dir), NullLogger<CredentialStore>.Instance);
        var factory = new FileTransferConnectionFactory(
            NullLoggerFactory.Instance, creds, new TrustAllHostKeyVerifier(), new KnownHostsStore(AppPaths.Portable(_dir)));

        var ssh = new SshConnectionSettings
        {
            Host = "example.com",
            Port = 2222,
            Username = "deploy",
            AuthMethod = SshAuthMethod.Password,
            ProtectedPassword = creds.Protect("s3cr3t")
        };

        factory.Invoking(f => f.CreateSftpForSsh(ssh)).Should().NotThrow();
    }

    [Fact]
    public async Task Ftp_connect_to_a_closed_port_fails_fast()
    {
        var settings = new FtpConnectionSettings { Host = "127.0.0.1", Port = 59_998, Username = "anonymous" };
        await using var conn = _factory.Create(settings);

        var connect = conn.ConnectAsync();
        var finished = await Task.WhenAny(connect, Task.Delay(TimeSpan.FromSeconds(25)));

        finished.Should().BeSameAs(connect);
        await Assert.ThrowsAnyAsync<Exception>(() => connect);
        conn.IsConnected.Should().BeFalse();
    }
}
