using System.Text;

namespace Windu;

public static class Format
{
    private static readonly string[] SiUnits   = { "  B", " kB", " MB", " GB", " TB", " PB", " EB" };
    private static readonly string[] IecUnits  = { "  B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };

    /// <summary>
    /// Formats a byte count the way ncdu does: a right-aligned number with at most
    /// one decimal, followed by a unit, e.g. "  1.4 GiB" or "999.9 KiB".
    /// </summary>
    public static string Size(long bytes, bool si = false)
    {
        var units = si ? SiUnits : IecUnits;
        double div = si ? 1000d : 1024d;
        double v = bytes;
        int u = 0;
        while (v >= div * 0.9995 && u < units.Length - 1) { v /= div; u++; }
        // Below the first unit boundary ncdu prints a plain integer.
        return u == 0
            ? $"{(long)v,5:0} {units[0]}"
            : $"{v,5:0.0} {units[u]}";
    }

    /// <summary>Plain grouped integer, e.g. "1,234,567".</summary>
    public static string Exact(long bytes) => bytes.ToString("N0");

    public static string Count(long n)
    {
        if (n < 1000) return n.ToString();
        double v = n;
        var units = new[] { "", "K", "M", "G", "T" };
        int u = 0;
        while (v >= 1000 && u < units.Length - 1) { v /= 1000; u++; }
        return $"{v:0.0}{units[u]}";
    }

    /// <summary>Renders the "[###   ]" percentage/size bar used in the listing.</summary>
    public static string Graph(long value, long max, int width)
    {
        if (width <= 0) return "";
        if (max <= 0) return new string(' ', width);
        int filled = (int)Math.Round((double)value / max * width);
        if (filled > width) filled = width;
        if (filled < 0) filled = 0;
        return new string('#', filled) + new string(' ', width - filled);
    }

    public static string Percent(long value, long max)
    {
        double p = max <= 0 ? 0 : (double)value / max * 100.0;
        return $"{p,5:0.0}%";
    }

    /// <summary>Truncates to <paramref name="width"/> columns, using an ellipsis when cut.</summary>
    public static string Fit(string s, int width)
    {
        if (width <= 0) return "";
        if (s.Length <= width) return s;
        if (width <= 3) return s[..width];
        return string.Concat(s.AsSpan(0, width - 3), "...");
    }

    public static string Pad(string s, int width)
    {
        if (s.Length >= width) return Fit(s, width);
        return s + new string(' ', width - s.Length);
    }

    /// <summary>Replaces control characters so a hostile filename cannot corrupt the screen.</summary>
    public static string Sanitize(string s)
    {
        bool needs = false;
        foreach (var c in s)
            if (char.IsControl(c)) { needs = true; break; }
        if (!needs) return s;
        var sb = new StringBuilder(s.Length);
        foreach (var c in s) sb.Append(char.IsControl(c) ? '?' : c);
        return sb.ToString();
    }
}
