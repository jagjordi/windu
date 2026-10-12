using System.Text;

namespace Windu;

/// <summary>
/// A non-interactive harness: renders the browser at a fixed size after replaying
/// a scripted key sequence, and dumps the screen as plain text. Used by the tests
/// to check the layout without a real console attached.
/// </summary>
public static class RenderTest
{
    public static int Run(FsEntry root, Settings cfg, int width, int height, string keys)
    {
        var browser = new Browser(root, cfg);
        var text = browser.RenderToText(width, height, ParseKeys(keys));
        Console.Out.Write(text);
        return 0;
    }

    /// <summary>
    /// Turns a key script into console key events. Named keys are written in
    /// angle brackets, e.g. "&lt;down&gt;&lt;down&gt;&lt;enter&gt;g?"; everything else is a literal character.
    /// </summary>
    public static List<ConsoleKeyInfo> ParseKeys(string s)
    {
        var list = new List<ConsoleKeyInfo>();
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '<')
            {
                int end = s.IndexOf('>', i);
                if (end < 0) throw new ArgumentException("unterminated key name in the key script.");
                var name = s.Substring(i + 1, end - i - 1).ToLowerInvariant();
                list.Add(Named(name));
                i = end;
            }
            else
            {
                list.Add(new ConsoleKeyInfo(s[i], CharToKey(s[i]), false, false, false));
            }
        }
        return list;
    }

    private static ConsoleKeyInfo Named(string name)
    {
        var key = name switch
        {
            "up" => ConsoleKey.UpArrow,
            "down" => ConsoleKey.DownArrow,
            "left" => ConsoleKey.LeftArrow,
            "right" => ConsoleKey.RightArrow,
            "enter" => ConsoleKey.Enter,
            "esc" => ConsoleKey.Escape,
            "home" => ConsoleKey.Home,
            "end" => ConsoleKey.End,
            "pgup" => ConsoleKey.PageUp,
            "pgdn" => ConsoleKey.PageDown,
            "del" => ConsoleKey.Delete,
            "backspace" => ConsoleKey.Backspace,
            "tab" => ConsoleKey.Tab,
            _ => throw new ArgumentException($"unknown key name '{name}'."),
        };
        return new ConsoleKeyInfo('\0', key, false, false, false);
    }

    private static ConsoleKey CharToKey(char c)
    {
        if (c is >= 'a' and <= 'z') return ConsoleKey.A + (c - 'a');
        if (c is >= 'A' and <= 'Z') return ConsoleKey.A + (c - 'A');
        if (c is >= '0' and <= '9') return ConsoleKey.D0 + (c - '0');
        return (ConsoleKey)0;
    }
}
