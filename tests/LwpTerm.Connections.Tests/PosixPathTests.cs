using FluentAssertions;
using LwpTerm.Core.Transfer;

namespace LwpTerm.Connections.Tests;

public class PosixPathTests
{
    [Theory]
    [InlineData("/", "file.txt", "/file.txt")]
    [InlineData("/home/user", "a", "/home/user/a")]
    [InlineData("/home/user/", "a", "/home/user/a")]
    [InlineData("", "a", "/a")]
    public void Combine_joins_with_a_single_slash(string dir, string name, string expected)
        => PosixPath.Combine(dir, name).Should().Be(expected);

    [Theory]
    [InlineData("/home/user/file", "/home/user")]
    [InlineData("/home/user/", "/home")]
    [InlineData("/home", "/")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    public void Parent_walks_up_one_segment(string path, string expected)
        => PosixPath.Parent(path).Should().Be(expected);

    [Theory]
    [InlineData("/home/user/file.txt", "file.txt")]
    [InlineData("/home/user/", "user")]
    [InlineData("/x", "x")]
    public void Name_returns_the_last_segment(string path, string expected)
        => PosixPath.Name(path).Should().Be(expected);
}
