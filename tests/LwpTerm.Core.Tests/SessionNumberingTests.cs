using FluentAssertions;
using LwpTerm.Core.Sessions;

namespace LwpTerm.Core.Tests;

public class SessionNumberingTests
{
    [Theory]
    [InlineData("SW13", "SW14")]
    [InlineData("web-09", "web-10")]
    [InlineData("rtr 1", "rtr 2")]
    [InlineData("core-switch-099", "core-switch-100")]
    [InlineData("fw01a", "fw02a")]
    [InlineData("router", "router 2")]
    public void NextName_bumps_the_trailing_number(string input, string expected)
        => SessionNumbering.NextName(input).Should().Be(expected);

    [Theory]
    [InlineData("10.0.0.13", "10.0.0.14")]
    [InlineData("192.168.1.254", "192.168.1.1")]
    [InlineData("192.168.1.1", "192.168.1.2")]
    [InlineData("host.example.com", "host.example.com")]
    [InlineData("", "")]
    public void NextHost_bumps_the_last_octet_of_an_ipv4(string input, string expected)
        => SessionNumbering.NextHost(input).Should().Be(expected);

    [Fact]
    public void ConnectionHost_reads_and_writes_host_for_host_protocols()
    {
        var ssh = new SshConnectionSettings { Host = "a" };
        ConnectionHost.Get(ssh).Should().Be("a");
        ConnectionHost.Set(ssh, "b");
        ssh.Host.Should().Be("b");

        ConnectionHost.HasHost(new SerialConnectionSettings()).Should().BeFalse();
        ConnectionHost.HasHost(new LocalShellConnectionSettings()).Should().BeFalse();
        ConnectionHost.HasHost(new RdpConnectionSettings()).Should().BeTrue();
    }
}
