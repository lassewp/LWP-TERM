using FluentAssertions;
using LwpTerm.Core.Sessions;

namespace LwpTerm.Core.Tests;

public class QuickConnectParserTests
{
    [Fact]
    public void Bare_host_is_ssh_on_22()
    {
        QuickConnectParser.TryParse("example.com", out var s).Should().BeTrue();
        s.Protocol.Should().Be(ProtocolType.Ssh);
        var ssh = s.Settings.Should().BeOfType<SshConnectionSettings>().Subject;
        ssh.Host.Should().Be("example.com");
        ssh.Port.Should().Be(22);
    }

    [Fact]
    public void User_at_host_colon_port()
    {
        QuickConnectParser.TryParse("deploy@web01:2222", out var s).Should().BeTrue();
        var ssh = (SshConnectionSettings)s.Settings;
        ssh.Username.Should().Be("deploy");
        ssh.Host.Should().Be("web01");
        ssh.Port.Should().Be(2222);
    }

    [Theory]
    [InlineData("telnet://10.0.0.1", ProtocolType.Telnet, 23)]
    [InlineData("rdp://10.0.0.5", ProtocolType.Rdp, 3389)]
    [InlineData("vnc://10.0.0.6:5901", ProtocolType.Vnc, 5901)]
    [InlineData("ftp://files.example.com", ProtocolType.Ftp, 21)]
    [InlineData("sftp://user@host", ProtocolType.Sftp, 22)]
    public void Scheme_prefixes_select_protocol_and_default_port(string input, ProtocolType protocol, int port)
    {
        QuickConnectParser.TryParse(input, out var s).Should().BeTrue();
        s.Protocol.Should().Be(protocol);
        GetPort(s).Should().Be(port);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ssh://")]
    [InlineData("user@")]
    public void Rejects_empty_or_hostless_input(string input)
        => QuickConnectParser.TryParse(input, out _).Should().BeFalse();

    private static int GetPort(SessionItem s) => s.Settings switch
    {
        SshConnectionSettings x => x.Port,
        TelnetConnectionSettings x => x.Port,
        RdpConnectionSettings x => x.Port,
        VncConnectionSettings x => x.Port,
        FtpConnectionSettings x => x.Port,
        SftpConnectionSettings x => x.Port,
        _ => -1
    };
}
