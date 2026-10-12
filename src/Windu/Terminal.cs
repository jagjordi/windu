using System.Text;

namespace Windu;

public enum Color
{
    Default,
    Header,     // black on cyan  — top and bottom bars
    Selected,   // black on cyan  — cursor row
    Normal,     // light grey on black
    Dir,        // bright blue
    Bold,       // bright white
    Graph,      // magenta bar
    Flag,       // yellow-ish flag column
    Error,      // red
    Info,       // window frames
}

public readonly struct Cell
{
    public readonly char Ch;
    public readonly Color Fg;
    public Cell(char ch, Color fg) { Ch = ch; Fg = fg; }
}

/// <summary>
/// A double-buffered character screen. Draw into the back buffer, then call
/// <see cref="Flush"/>; only the runs that actually changed are written out,
/// which keeps redraws flicker-free over slow terminals and RDP.
/// </summary>
public sealed class Screen
{
    public int Width { get; private set; }
    public int Height { get; private set; }

    private Cell[] _front = Array.Empty<Cell>();
    private Cell[] _back = Array.Empty<Cell>();
    private readonly StringBuilder _out = new(1 << 16);
    private bool _forceFull = true;

    /// <summary>When set, the screen keeps a fixed size and ignores the real console.</summary>
    private readonly bool _fixedSize;

    public Screen() => Resize();

    /// <summary>Creates an offscreen buffer of a fixed size, for rendering without a console.</summary>
    public Screen(int width, int height)
    {
        _fixedSize = true;
        Width = Math.Max(width, 20);
        Height = Math.Max(height, 6);
        _front = new Cell[Width * Height];
        _back = new Cell[Width * Height];
        Clear();
    }

    /// <summary>Re-reads the console size. Returns true if it changed.</summary>
    public bool Resize()
    {
        if (_fixedSize) return false;

        int w, h;
        try
        {
            w = Math.Max(Console.WindowWidth, 20);
            h = Math.Max(Console.WindowHeight, 6);
        }
        catch { w = 80; h = 25; }

        if (w == Width && h == Height) return false;
        Width = w; Height = h;
        _front = new Cell[w * h];
        _back = new Cell[w * h];
        _forceFull = true;
        return true;
    }

    public void Clear()
    {
        for (int i = 0; i < _back.Length; i++) _back[i] = new Cell(' ', Color.Normal);
    }

    public void Put(int x, int y, char ch, Color color)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return;
        // Control characters would let a filename move the cursor or reset colours.
        _back[y * Width + x] = new Cell(char.IsControl(ch) ? '?' : ch, color);
    }

    public void Write(int x, int y, string text, Color color)
    {
        if (y < 0 || y >= Height) return;
        foreach (var ch in text)
        {
            if (x >= Width) break;
            if (x >= 0) _back[y * Width + x] = new Cell(char.IsControl(ch) ? '?' : ch, color);
            x++;
        }
    }

    /// <summary>Fills a row from x to the right edge.</summary>
    public void Fill(int x, int y, int width, char ch, Color color)
    {
        for (int i = 0; i < width; i++) Put(x + i, y, ch, color);
    }

    public void Flush()
    {
        if (_fixedSize)
        {
            // Offscreen: just promote the back buffer so ToText() sees the result.
            Array.Copy(_back, _front, _back.Length);
            return;
        }

        _out.Clear();
        var lastColor = (Color)(-1);
        int lastX = -1, lastY = -1;

        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                var cell = _back[i];
                if (!_forceFull && cell.Ch == _front[i].Ch && cell.Fg == _front[i].Fg) continue;

                // The bottom-right cell is skipped: writing it scrolls some terminals.
                if (y == Height - 1 && x == Width - 1) continue;

                if (y != lastY || x != lastX)
                {
                    _out.Append("\x1b[").Append(y + 1).Append(';').Append(x + 1).Append('H');
                    lastY = y; lastX = x;
                }
                if (cell.Fg != lastColor)
                {
                    _out.Append(Ansi(cell.Fg));
                    lastColor = cell.Fg;
                }
                _out.Append(cell.Ch);
                lastX++;
                _front[i] = cell;
            }
        }

        if (_out.Length > 0)
        {
            _out.Append("\x1b[0m");
            Console.Out.Write(_out);
            Console.Out.Flush();
        }
        _forceFull = false;
    }

    /// <summary>Dumps the current screen contents as plain text, one line per row.</summary>
    public string ToText()
    {
        var sb = new StringBuilder((Width + 1) * Height);
        for (int y = 0; y < Height; y++)
        {
            int end = Width;
            while (end > 0 && _front[y * Width + end - 1].Ch == ' ') end--;
            for (int x = 0; x < end; x++) sb.Append(_front[y * Width + x].Ch);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Ansi(Color c) => c switch
    {
        Color.Header   => "\x1b[0;30;46m",
        Color.Selected => "\x1b[0;30;46m",
        Color.Dir      => "\x1b[0;1;34m",
        Color.Bold     => "\x1b[0;1;37m",
        Color.Graph    => "\x1b[0;35m",
        Color.Flag     => "\x1b[0;33m",
        Color.Error    => "\x1b[0;1;31m",
        Color.Info     => "\x1b[0;1;36m",
        _              => "\x1b[0;37;40m",
    };
}
