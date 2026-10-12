using Windu;
using Xunit;

namespace Windu.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(0, "    0   B")]
    [InlineData(1, "    1   B")]
    [InlineData(1023, " 1023   B")]
    [InlineData(1024, "  1.0 KiB")]
    [InlineData(1536, "  1.5 KiB")]
    [InlineData(1024 * 1024, "  1.0 MiB")]
    [InlineData(1024L * 1024 * 1024, "  1.0 GiB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "  1.0 TiB")]
    public void Size_uses_base2_units(long bytes, string expected)
        => Assert.Equal(expected, Format.Size(bytes));

    [Theory]
    [InlineData(1000, "  1.0  kB")]
    [InlineData(1_000_000, "  1.0  MB")]
    public void Size_uses_si_units_when_asked(long bytes, string expected)
        => Assert.Equal(expected, Format.Size(bytes, si: true));

    [Fact]
    public void Size_columns_line_up()
    {
        // Every rendered size must occupy the same width so the columns align.
        long[] samples = { 0, 999, 1024, 999_999, 1L << 30, 1L << 50 };
        var widths = samples.Select(s => Format.Size(s).Length).Distinct();
        Assert.Single(widths);
    }

    [Theory]
    [InlineData(0, 100, 10, "          ")]
    [InlineData(100, 100, 10, "##########")]
    [InlineData(50, 100, 10, "#####     ")]
    public void Graph_fills_proportionally(long v, long max, int w, string expected)
        => Assert.Equal(expected, Format.Graph(v, max, w));

    [Fact]
    public void Graph_never_exceeds_its_width()
    {
        // A child can exceed the recorded maximum after a deletion; clamp rather than overflow.
        Assert.Equal(5, Format.Graph(999, 100, 5).Length);
        Assert.Equal("#####", Format.Graph(999, 100, 5));
    }

    [Fact]
    public void Graph_handles_zero_maximum()
        => Assert.Equal("     ", Format.Graph(0, 0, 5));

    [Theory]
    [InlineData("abc", 10, "abc")]
    [InlineData("abcdefghij", 5, "ab...")]
    [InlineData("abc", 2, "ab")]
    [InlineData("abc", 0, "")]
    public void Fit_truncates_with_an_ellipsis(string s, int w, string expected)
        => Assert.Equal(expected, Format.Fit(s, w));

    [Fact]
    public void Sanitize_replaces_control_characters()
    {
        // A filename carrying an escape sequence must not be able to move the cursor.
        // Built from a char so the trailing 'b' is not absorbed into a hex escape.
        var esc = ((char)27).ToString();
        Assert.Equal("a?b", Format.Sanitize("a" + esc + "b"));
        Assert.Equal("plain", Format.Sanitize("plain"));
    }

    [Fact]
    public void Percent_is_bounded_and_formatted()
    {
        Assert.Equal("100.0%", Format.Percent(10, 10));
        Assert.Equal("  0.0%", Format.Percent(0, 10));
        Assert.Equal("  0.0%", Format.Percent(5, 0));
    }
}
