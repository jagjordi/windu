namespace Windu;

/// <summary>
/// Centred, framed modal windows drawn on top of the current screen contents —
/// the equivalent of ncdu's ncurses subwindows.
/// </summary>
public static class Popup
{
    /// <summary>
    /// When set, popups take their keystrokes from here instead of the console.
    /// The render harness uses this to drive dialogs without a terminal attached.
    /// </summary>
    public static Func<ConsoleKeyInfo>? KeySource;

    /// <summary>
    /// Set by the harness alongside <see cref="KeySource"/>. It reports whether the
    /// key script is exhausted; when it is, the popup on screen is captured before
    /// being dismissed.
    /// </summary>
    public static Func<bool>? IsExhausted;

    /// <summary>Called just before an exhausted script dismisses a popup.</summary>
    public static Action? OnExhausted;

    private static ConsoleKeyInfo ReadKey()
    {
        if (KeySource is null) return Console.ReadKey(intercept: true);
        if (IsExhausted?.Invoke() == true) OnExhausted?.Invoke();
        return KeySource();
    }

    private static (int x, int y, int w, int h) Frame(Screen s, string title, string[] lines, int extraRows)
    {
        int contentW = title.Length;
        foreach (var l in lines) contentW = Math.Max(contentW, l.Length);

        int w = Math.Min(s.Width - 2, contentW + 4);
        int h = Math.Min(s.Height - 2, lines.Length + extraRows + 2);
        int x = (s.Width - w) / 2;
        int y = (s.Height - h) / 2;
        if (x < 0) x = 0;
        if (y < 0) y = 0;

        // Border
        for (int i = 0; i < h; i++)
        {
            s.Fill(x, y + i, w, ' ', Color.Normal);
            s.Put(x, y + i, '|', Color.Info);
            s.Put(x + w - 1, y + i, '|', Color.Info);
        }
        for (int i = 0; i < w; i++)
        {
            s.Put(x + i, y, '-', Color.Info);
            s.Put(x + i, y + h - 1, '-', Color.Info);
        }
        s.Put(x, y, '+', Color.Info);
        s.Put(x + w - 1, y, '+', Color.Info);
        s.Put(x, y + h - 1, '+', Color.Info);
        s.Put(x + w - 1, y + h - 1, '+', Color.Info);

        if (title.Length + 4 <= w)
            s.Write(x + 2, y, title, Color.Bold);

        for (int i = 0; i < lines.Length && y + 1 + i < y + h - 1; i++)
            s.Write(x + 2, y + 1 + i, Format.Fit(lines[i], w - 4), Color.Normal);

        return (x, y, w, h);
    }

    /// <summary>
    /// Shows a window and waits for a key. When the content is taller than the
    /// terminal, the window scrolls with the arrow keys instead of silently
    /// truncating, and the footer shows the position.
    /// </summary>
    public static void Message(Screen s, string title, string[] lines)
    {
        int offset = 0;
        while (true)
        {
            // The window is inset by one row top and bottom, and Frame draws two
            // border rows plus the blank spacer and the prompt line beneath the text.
            int visible = Math.Max(1, Math.Min(lines.Length, s.Height - 6));
            int maxOffset = Math.Max(0, lines.Length - visible);
            offset = Math.Clamp(offset, 0, maxOffset);

            var page = lines.Skip(offset).Take(visible).ToList();
            page.Add("");
            page.Add(maxOffset > 0
                ? $" Line {offset + 1}-{offset + visible} of {lines.Length}. Up/down to scroll, q to close."
                : " Press any key to continue.");

            Frame(s, title, page.ToArray(), extraRows: 0);
            s.Flush();

            var k = ReadKey();
            if (maxOffset == 0) return;

            switch (k.Key)
            {
                case ConsoleKey.UpArrow: offset--; break;
                case ConsoleKey.DownArrow: offset++; break;
                case ConsoleKey.PageUp: offset -= visible; break;
                case ConsoleKey.PageDown: offset += visible; break;
                case ConsoleKey.Home: offset = 0; break;
                case ConsoleKey.End: offset = maxOffset; break;
                default:
                    if (k.KeyChar == 'k') offset--;
                    else if (k.KeyChar == 'j') offset++;
                    else return;
                    break;
            }
        }
    }

    /// <summary>
    /// Shows a yes/no window. Left/right or y/n move between the buttons;
    /// enter confirms, escape cancels.
    /// </summary>
    public static bool Confirm(Screen s, string title, string[] lines, bool defaultYes)
    {
        bool yes = defaultYes;
        while (true)
        {
            var (x, y, w, h) = Frame(s, title, lines, extraRows: 2);

            int by = y + h - 2;
            const string yesLabel = "  yes  ";
            const string noLabel = "  no  ";
            int bx = x + (w - (yesLabel.Length + noLabel.Length + 2)) / 2;
            s.Write(bx, by, yesLabel, yes ? Color.Selected : Color.Normal);
            s.Write(bx + yesLabel.Length + 2, by, noLabel, yes ? Color.Normal : Color.Selected);
            s.Flush();

            var k = ReadKey();
            switch (k.Key)
            {
                case ConsoleKey.LeftArrow:
                case ConsoleKey.RightArrow:
                case ConsoleKey.Tab:
                    yes = !yes;
                    continue;
                case ConsoleKey.Enter:
                    return yes;
                case ConsoleKey.Escape:
                    return false;
            }
            switch (char.ToLowerInvariant(k.KeyChar))
            {
                case 'y': return true;
                case 'n': return false;
                case 'h': yes = true; continue;
                case 'l': yes = false; continue;
            }
        }
    }
}
