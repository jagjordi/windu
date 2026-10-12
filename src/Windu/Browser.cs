namespace Windu;

public enum SortField { Size, Name, ItemCount, MTime }

public sealed class Settings
{
    public SortField Sort = SortField.Size;
    public bool SortDescending = true;
    public bool ShowApparentSize;      // 'a' toggles apparent size vs disk usage
    public bool ShowGraph = true;
    public bool ShowPercent;
    public bool ShowHidden = true;
    public bool SiUnits;
    public bool DirsFirst;
}

/// <summary>
/// The interactive file browser: a listing of one directory at a time, with a
/// cursor, sorting, deletion and the informational popups ncdu provides.
/// </summary>
public sealed class Browser
{
    private Screen _s = new();
    private readonly Settings _cfg;
    private readonly FsEntry _root;

    private FsEntry _cwd;
    private List<FsEntry> _view = new();
    private int _cursor;
    private int _top;

    private string _message = "";
    private bool _quit;

    // Remembers the cursor position per directory so going back returns you where you were.
    private readonly Dictionary<FsEntry, string> _lastSelected = new();

    private const int GraphWidth = 10;

    public Browser(FsEntry root, Settings cfg)
    {
        _root = root;
        _cwd = root;
        _cfg = cfg;
        Rebuild();
    }

    public void Run()
    {
        Console.CursorVisible = false;
        try
        {
            Draw();
            while (!_quit)
            {
                var key = Console.ReadKey(intercept: true);
                _message = "";
                HandleKey(key);
                if (_s.Resize()) ClampScroll();
                Draw();
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    // ---------------------------------------------------------------- listing

    private long SizeOf(FsEntry e) => _cfg.ShowApparentSize ? e.Size : e.DiskSize;

    private void Rebuild()
    {
        var children = _cwd.Children ?? new List<FsEntry>();
        IEnumerable<FsEntry> q = children;
        if (!_cfg.ShowHidden)
            q = q.Where(c => !c.Name.StartsWith('.'));

        Comparison<FsEntry> cmp = _cfg.Sort switch
        {
            SortField.Name      => (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
            SortField.ItemCount => (a, b) => a.ItemCount.CompareTo(b.ItemCount),
            SortField.MTime     => (a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime),
            _                   => (a, b) => SizeOf(a).CompareTo(SizeOf(b)),
        };

        var list = q.ToList();
        list.Sort((a, b) =>
        {
            if (_cfg.DirsFirst && a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            int r = cmp(a, b);
            if (_cfg.SortDescending) r = -r;
            // Ties break on name so the order stays stable between redraws.
            return r != 0 ? r : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        _view = list;
        ClampScroll();
    }

    private int ListHeight => Math.Max(1, _s.Height - 3);

    private void ClampScroll()
    {
        if (_cursor >= _view.Count) _cursor = _view.Count - 1;
        int minCursor = ReferenceEquals(_cwd, _root) ? 0 : -1;
        if (_cursor < minCursor) _cursor = minCursor;

        int h = ListHeight;
        if (_cursor < _top) _top = Math.Max(0, _cursor);
        if (_cursor >= _top + h) _top = _cursor - h + 1;
        if (_top > _view.Count - h) _top = _view.Count - h;
        if (_top < 0) _top = 0;
    }

    private FsEntry? Current => _cursor >= 0 && _cursor < _view.Count ? _view[_cursor] : null;

    // ---------------------------------------------------------------- drawing

    private void Draw()
    {
        _s.Clear();
        DrawHeader();
        DrawList();
        DrawFooter();
        _s.Flush();
    }

    private void DrawHeader()
    {
        int w = _s.Width;
        var title = $"windu {Program.Version} ~ Use the arrow keys to navigate, press ? for help";
        _s.Fill(0, 0, w, ' ', Color.Header);
        _s.Write(0, 0, Format.Fit(title, w), Color.Header);

        var path = Format.Sanitize(_cwd.FullPath);
        _s.Fill(0, 1, w, '-', Color.Normal);
        var label = "--- " + Format.Fit(path, Math.Max(0, w - 6)) + " ";
        _s.Write(0, 1, label, Color.Bold);
    }

    private void DrawList()
    {
        int h = ListHeight;
        int w = _s.Width;
        long max = _view.Count > 0 ? _view.Max(SizeOf) : 0;
        long parentTotal = SizeOf(_cwd);

        int y0 = 2;
        int row = 0;

        // The ".." entry sits above the first item whenever we are not at the root.
        if (!ReferenceEquals(_cwd, _root) && _top == 0)
        {
            bool sel = _cursor == -1;
            var c = sel ? Color.Selected : Color.Dir;
            _s.Fill(0, y0, w, ' ', sel ? Color.Selected : Color.Normal);
            _s.Write(2, y0, "/..", c);
            row++;
        }

        for (int i = _top; i < _view.Count && row < h; i++, row++)
            DrawRow(y0 + row, _view[i], i == _cursor, max, parentTotal, w);

        if (_view.Count == 0)
            _s.Write(2, y0 + row, " no items", Color.Normal);
    }

    private void DrawRow(int y, FsEntry e, bool selected, long max, long parentTotal, int w)
    {
        var baseColor = selected ? Color.Selected
                      : e.IsDirectory ? Color.Dir
                      : Color.Normal;

        _s.Fill(0, y, w, ' ', selected ? Color.Selected : Color.Normal);

        int x = 0;

        // Flag column: ! read error, @ link, H hard link, e empty dir, . partial error
        char flag = ' ';
        if (e.ReadError) flag = '!';
        else if (e.IsLink) flag = '@';
        else if (e.IsDuplicate) flag = 'H';
        else if (e.IsDirectory && (e.Children?.Count ?? 0) == 0) flag = 'e';
        else if (e.IsDirectory && e.HasError) flag = '.';
        _s.Put(x, y, flag, selected ? Color.Selected : Color.Flag);
        x += 2;

        var size = Format.Size(SizeOf(e), _cfg.SiUnits);
        _s.Write(x, y, size, selected ? Color.Selected : Color.Bold);
        x += size.Length + 1;

        if (_cfg.ShowPercent)
        {
            _s.Write(x, y, Format.Percent(SizeOf(e), parentTotal), selected ? Color.Selected : Color.Normal);
            x += 7;
        }
        if (_cfg.ShowGraph && w > 40)
        {
            _s.Put(x, y, '[', selected ? Color.Selected : Color.Normal);
            _s.Write(x + 1, y, Format.Graph(SizeOf(e), max, GraphWidth), selected ? Color.Selected : Color.Graph);
            _s.Put(x + 1 + GraphWidth, y, ']', selected ? Color.Selected : Color.Normal);
            x += GraphWidth + 3;
        }

        // Directories get a leading slash, as in ncdu.
        var name = Format.Sanitize(e.Name);
        var label = e.IsDirectory ? "/" + name : name;
        _s.Write(x, y, Format.Fit(label, Math.Max(0, w - x)), baseColor);
    }

    private void DrawFooter()
    {
        int w = _s.Width;
        int y = _s.Height - 1;
        _s.Fill(0, y, w, ' ', Color.Header);

        if (!string.IsNullOrEmpty(_message))
        {
            _s.Write(0, y, Format.Fit(" " + _message, w), Color.Header);
            return;
        }

        var total = Format.Size(SizeOf(_cwd), _cfg.SiUnits).TrimStart();
        var left = $" Total disk usage: {total}   Apparent size: {Format.Size(_cwd.Size, _cfg.SiUnits).TrimStart()}   Items: {_cwd.ItemCount - 1}";
        _s.Write(0, y, Format.Fit(left, w), Color.Header);

        var right = _cfg.ShowApparentSize ? "[apparent size] " : "[disk usage] ";
        if (left.Length + right.Length < w)
            _s.Write(w - right.Length, y, right, Color.Header);
    }

    // ------------------------------------------------------------------ keys

    private void HandleKey(ConsoleKeyInfo k)
    {
        int h = ListHeight;
        int minCursor = ReferenceEquals(_cwd, _root) ? 0 : -1;

        switch (k.Key)
        {
            case ConsoleKey.UpArrow:
                if (_cursor > minCursor) _cursor--;
                ClampScroll();
                return;

            case ConsoleKey.DownArrow:
                if (_cursor < _view.Count - 1) _cursor++;
                ClampScroll();
                return;

            case ConsoleKey.PageUp:
                _cursor = Math.Max(minCursor, _cursor - h);
                ClampScroll();
                return;

            case ConsoleKey.PageDown:
                _cursor = Math.Min(_view.Count - 1, _cursor + h);
                ClampScroll();
                return;

            case ConsoleKey.Home:
                _cursor = minCursor; _top = 0;
                return;

            case ConsoleKey.End:
                _cursor = _view.Count - 1; ClampScroll();
                return;

            case ConsoleKey.RightArrow:
            case ConsoleKey.Enter:
                Descend();
                return;

            case ConsoleKey.LeftArrow:
            case ConsoleKey.Backspace:
                Ascend();
                return;

            case ConsoleKey.Delete:
                DeleteSelected();
                return;

            case ConsoleKey.Escape:
                return;
        }

        HandleChar(k);
    }

    private void HandleChar(ConsoleKeyInfo k)
    {
        switch (k.KeyChar)
        {
            case 'q': _quit = true; break;
            case '?': ShowHelp(); break;

            // vi-style movement
            case 'j': if (_cursor < _view.Count - 1) _cursor++; ClampScroll(); break;
            case 'k': if (_cursor > (ReferenceEquals(_cwd, _root) ? 0 : -1)) _cursor--; ClampScroll(); break;
            case 'h': Ascend(); break;
            case 'l': Descend(); break;

            case 'n': SetSort(SortField.Name); break;
            case 's': SetSort(SortField.Size); break;
            case 'C': SetSort(SortField.ItemCount); break;
            case 'M': SetSort(SortField.MTime); break;

            case 'e':
                _cfg.ShowHidden = !_cfg.ShowHidden;
                Rebuild();
                _message = _cfg.ShowHidden ? "Showing hidden items." : "Hiding hidden items.";
                break;

            case 'a':
                _cfg.ShowApparentSize = !_cfg.ShowApparentSize;
                Rebuild();
                _message = _cfg.ShowApparentSize ? "Showing apparent sizes." : "Showing disk usage.";
                break;

            case 'g':
                // Cycle: graph -> graph+percent -> percent -> none -> graph
                if (_cfg.ShowGraph && !_cfg.ShowPercent) _cfg.ShowPercent = true;
                else if (_cfg.ShowGraph && _cfg.ShowPercent) _cfg.ShowGraph = false;
                else if (!_cfg.ShowGraph && _cfg.ShowPercent) _cfg.ShowPercent = false;
                else _cfg.ShowGraph = true;
                break;

            case 'i': ShowInfo(); break;
            case 'd': DeleteSelected(); break;

            case 't':
                _cfg.DirsFirst = !_cfg.DirsFirst;
                Rebuild();
                _message = _cfg.DirsFirst ? "Directories sorted before files." : "Directories and files sorted together.";
                break;

            case 'u':
                _cfg.SiUnits = !_cfg.SiUnits;
                _message = _cfg.SiUnits ? "Using SI units (kB, MB)." : "Using base-2 units (KiB, MiB).";
                break;

            case 'b': OpenShell(); break;
        }
    }

    private void SetSort(SortField f)
    {
        if (_cfg.Sort == f) _cfg.SortDescending = !_cfg.SortDescending;
        else { _cfg.Sort = f; _cfg.SortDescending = f != SortField.Name; }
        Rebuild();
        var dir = _cfg.SortDescending ? "descending" : "ascending";
        _message = $"Sorting by {f.ToString().ToLowerInvariant()} ({dir}).";
    }

    private void Descend()
    {
        if (_cursor == -1) { Ascend(); return; }
        var e = Current;
        if (e is null || !e.IsDirectory) return;
        if (e.IsLink) { _message = "Not descending into a link."; return; }

        _lastSelected[_cwd] = e.Name;
        _cwd = e;
        _cursor = 0; _top = 0;
        Rebuild();
        RestoreCursor();
    }

    private void Ascend()
    {
        if (ReferenceEquals(_cwd, _root)) return;
        var child = _cwd;
        _cwd = _cwd.Parent!;
        _top = 0;
        Rebuild();
        // Land back on the directory we just came out of.
        int idx = _view.FindIndex(x => ReferenceEquals(x, child));
        _cursor = idx >= 0 ? idx : 0;
        ClampScroll();
    }

    private void RestoreCursor()
    {
        if (_lastSelected.TryGetValue(_cwd, out var name))
        {
            int idx = _view.FindIndex(x => x.Name == name);
            if (idx >= 0) { _cursor = idx; ClampScroll(); }
        }
    }

    // -------------------------------------------------------------- deletion

    private void DeleteSelected()
    {
        var e = Current;
        if (e is null) { _message = "Nothing selected."; return; }

        var path = e.FullPath;
        var what = e.IsLink ? "link" : e.IsDirectory ? "directory" : "file";
        int wrap = Math.Max(20, _s.Width - 14);

        var lines = new List<string>
        {
            $"Are you sure you want to delete this {what}?",
            "",
            Format.Fit(path, wrap),
            "",
        };
        if (e.IsDirectory && !e.IsLink && e.ItemCount > 1)
            lines.Add($"It contains {e.ItemCount - 1} item(s), {Format.Size(SizeOf(e), _cfg.SiUnits).Trim()}.");
        else
            lines.Add($"Size: {Format.Size(SizeOf(e), _cfg.SiUnits).Trim()}");

        if (!Popup.Confirm(_s, " Confirm delete ", lines.ToArray(), defaultYes: false))
        {
            _message = "Delete cancelled.";
            return;
        }

        try
        {
            if (e.IsDirectory && !e.IsLink) Directory.Delete(Native.Prefix(path), recursive: true);
            else if (e.IsDirectory) Directory.Delete(Native.Prefix(path));
            else File.Delete(Native.Prefix(path));
        }
        catch (Exception ex)
        {
            Popup.Message(_s, " Delete failed ", new[]
            {
                Format.Fit(path, wrap),
                "",
                Format.Fit(ex.Message, wrap),
            });
            return;
        }

        // Detach the node and correct every ancestor's running totals.
        long dSize = e.Size, dDisk = e.DiskSize, dItems = e.ItemCount;
        foreach (var anc in e.Ancestors())
        {
            anc.Size -= dSize;
            anc.DiskSize -= dDisk;
            anc.ItemCount -= dItems;
        }
        e.Parent?.Children?.Remove(e);
        _lastSelected.Remove(e);

        Rebuild();
        ClampScroll();
        _message = $"Deleted {Format.Fit(path, Math.Max(20, _s.Width - 12))}";
    }

    // ---------------------------------------------------------------- popups

    private void ShowInfo()
    {
        var e = Current;
        if (e is null) return;

        var lines = new List<string>
        {
            $"         Name: {Format.Fit(e.Name, 50)}",
            $"         Path: {Format.Fit(e.FullPath, 50)}",
            $"         Type: {(e.IsLink ? "link" : e.IsDirectory ? "directory" : "regular file")}",
            "",
            $"   Disk usage: {Format.Size(e.DiskSize, _cfg.SiUnits).Trim(),-12} ({Format.Exact(e.DiskSize)} B)",
            $"Apparent size: {Format.Size(e.Size, _cfg.SiUnits).Trim(),-12} ({Format.Exact(e.Size)} B)",
        };
        if (e.IsDirectory)
            lines.Add($"        Items: {e.ItemCount - 1}");
        if (e.LastWriteTime != default)
            lines.Add($"     Modified: {e.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
        if (e.IsDuplicate)
        {
            lines.Add("");
            lines.Add("This is a hard link counted elsewhere in the tree,");
            lines.Add("so its size is excluded from the totals.");
        }
        if (e.ReadError)
        {
            lines.Add("");
            lines.Add("Some files here could not be read; totals may be low.");
        }

        Popup.Message(_s, " Item info ", lines.ToArray());
    }

    private void ShowHelp()
    {
        Popup.Message(_s, " windu help ", new[]
        {
            " up, down / k, j      Cycle through the items",
            " right, enter / l     Open the selected directory",
            " left, backspace / h  Go to the parent directory",
            " home, end            Jump to the first or last item",
            " page up, page down   Scroll a whole screen",
            "",
            " n                    Sort by name (press again to reverse)",
            " s                    Sort by size (press again to reverse)",
            " C                    Sort by item count",
            " M                    Sort by modification time",
            " t                    Toggle directories before files",
            "",
            " a                    Toggle apparent size and disk usage",
            " g                    Cycle the percentage and graph columns",
            " u                    Toggle SI (kB) and base-2 (KiB) units",
            " e                    Show or hide hidden items",
            "",
            " i                    Show information about the selected item",
            " d, delete            Delete the selected file or directory",
            " b                    Spawn a shell in the current directory",
            " q                    Quit windu",
        });
    }

    /// <summary>
    /// Renders the browser offscreen at a fixed size after replaying a key script.
    /// Used by the tests; it exercises exactly the same draw and key-handling code
    /// as the interactive loop.
    /// </summary>
    public string RenderToText(int width, int height, List<ConsoleKeyInfo> keys)
    {
        _s = new Screen(width, height);
        Rebuild();
        Draw();   // popups overlay the listing, so paint it first

        // Popups take their keys from the same script; an exhausted script closes them.
        var queue = new Queue<ConsoleKeyInfo>(keys);
        var escape = new ConsoleKeyInfo('\0', ConsoleKey.Escape, false, false, false);
        Popup.KeySource = () => queue.Count > 0 ? queue.Dequeue() : escape;

        // If the script runs out while a popup is on screen, capture that popup
        // rather than the listing underneath it.
        string? captured = null;
        Popup.IsExhausted = () => queue.Count == 0;
        Popup.OnExhausted = () => captured ??= _s.ToText();

        try
        {
            while (queue.Count > 0)
            {
                _message = "";
                HandleKey(queue.Dequeue());
                ClampScroll();
            }
        }
        finally
        {
            Popup.KeySource = null;
            Popup.IsExhausted = null;
            Popup.OnExhausted = null;
        }

        if (captured is not null) return captured;
        Draw();
        return _s.ToText();
    }

    private void OpenShell()
    {
        var dir = _cwd.FullPath;
        try
        {
            Console.Write("\x1b[0m\x1b[2J\x1b[H");
            Console.CursorVisible = true;
            Console.WriteLine($"Spawning a shell in {dir}. Type 'exit' to return to windu.");
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                WorkingDirectory = dir,
                UseShellExecute = false,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit();
        }
        catch (Exception ex)
        {
            _message = "Could not spawn a shell: " + ex.Message;
        }
        finally
        {
            Console.CursorVisible = false;
            _s.Resize();
            Console.Write("\x1b[2J");
        }
    }
}
