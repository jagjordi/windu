using Windu;
using Xunit;

namespace Windu.Tests;

public class GlobTests
{
    [Theory]
    [InlineData("node_modules", "node_modules", true)]
    [InlineData("node_modules", "node_*", true)]
    [InlineData("a.txt", "*.txt", true)]
    [InlineData("a.txt", "*.md", false)]
    [InlineData("abc", "a?c", true)]
    [InlineData("ac", "a?c", false)]
    [InlineData("a.txt", "*", true)]
    [InlineData("", "*", true)]
    [InlineData("anything", "**", true)]
    [InlineData("file1", "file[0-9]", true)]
    [InlineData("filex", "file[0-9]", false)]
    [InlineData("filex", "file[!0-9]", true)]
    public void Matches_shell_style_patterns(string input, string pattern, bool expected)
        => Assert.Equal(expected, Glob.IsMatch(input, pattern));

    [Fact]
    public void Matching_is_case_insensitive_like_windows()
    {
        Assert.True(Glob.IsMatch("NODE_MODULES", "node_modules"));
        Assert.True(Glob.IsMatch("A.TXT", "*.txt"));
    }

    [Fact]
    public void Unterminated_class_does_not_throw()
        => Assert.False(Glob.IsMatch("abc", "a[bc"));

    [Fact]
    public void Leading_and_trailing_wildcards_work()
    {
        Assert.True(Glob.IsMatch("xxcachexx", "*cache*"));
        Assert.False(Glob.IsMatch("xxxx", "*cache*"));
    }
}
