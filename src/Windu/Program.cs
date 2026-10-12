using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Windu;

public static class Program
{
    /// <summary>
    /// Taken from the assembly so that a release build stamped with
    /// <c>-p:Version=…</c> reports the version it was actually published as.
    /// </summary>
    public static readonly string Version = ReadVersion();

    private static string ReadVersion()
    {
        var info = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        // The informational version carries a build suffix like "1.0.0+a1b2c3d".
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }

        return typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    public static int Main(string[] rawArgs)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (!VirtualTerminal.TryEnable())
        {
            Console.Error.WriteLine("windu: this terminal does not support ANSI escape sequences.");
            Console.Error.WriteLine("Use Windows Terminal, or run on Windows 10 1511 or newer.");
            return 1;
        }

        Options opts;
        try { opts = Options.Parse(rawArgs); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("windu: " + ex.Message);
            Console.Error.WriteLine("Try 'windu --help' for more information.");
            return 1;
        }

        if (opts.ShowHelp) { Options.PrintHelp(); return 0; }
        if (opts.ShowVersion) { Console.WriteLine("windu " + Version); return 0; }

        try
        {
            FsEntry root;

            if (opts.ImportFile is not null)
            {
                root = ExportJson.Read(opts.ImportFile, out _);
            }
            else
            {
                if (!Directory.Exists(opts.Path))
                {
                    Console.Error.WriteLine($"windu: cannot open '{opts.Path}': no such directory.");
                    return 1;
                }
                root = RunScan(opts);
            }

            if (opts.ExportFile is not null)
            {
                ExportJson.Write(root, opts.ExportFile, root.Name);
                if (opts.ExportFile != "-")
                    Console.Error.WriteLine($"windu: wrote {opts.ExportFile}");
                return 0;
            }

            if (opts.RenderKeys is not null)
                return RenderTest.Run(root, opts.Settings, opts.RenderWidth, opts.RenderHeight, opts.RenderKeys);

            if (Console.IsOutputRedirected)
            {
                Console.Error.WriteLine("windu: output is not a terminal; use -o to export instead.");
                return 1;
            }

            AltScreen.Enter();
            try
            {
                new Browser(root, opts.Settings).Run();
            }
            finally
            {
                AltScreen.Leave();
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            AltScreen.Leave();
            Console.Error.WriteLine("windu: cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            AltScreen.Leave();
            Console.Error.WriteLine("windu: " + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// Runs the scan on a worker thread while the main thread paints the
    /// progress window, the same way ncdu shows its live counters.
    /// </summary>
    private static FsEntry RunScan(Options opts)
    {
        var progress = new ScanProgress();
        using var cts = new CancellationTokenSource();

        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cts.Cancel(); };
        Console.CancelKeyPress += onCancel;

        FsEntry? result = null;
        Exception? failure = null;

        var worker = new Thread(() =>
        {
            try
            {
                var scanner = new Scanner(opts.ScanOptions, progress, opts.Path, cts.Token);
                result = scanner.Scan(opts.Path);
            }
            catch (Exception ex) { failure = ex; }
        }, maxStackSize: 32 * 1024 * 1024); // deep trees need more than the default stack

        var quiet = opts.Quiet || Console.IsOutputRedirected;
        if (!quiet) AltScreen.Enter();

        worker.Start();
        var sw = Stopwatch.StartNew();
        try
        {
            if (quiet)
            {
                worker.Join();
            }
            else
            {
                var screen = new Screen();
                Console.CursorVisible = false;
                while (!worker.Join(100))
                {
                    screen.Resize();
                    DrawProgress(screen, progress, opts.Path, sw.Elapsed);
                    // Any key press cancels an in-flight scan.
                    while (Console.KeyAvailable)
                    {
                        var k = Console.ReadKey(intercept: true);
                        if (k.Key == ConsoleKey.Q || k.KeyChar == 'q') cts.Cancel();
                    }
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            if (!quiet) AltScreen.Leave();
        }

        if (failure is not null) throw failure;
        cts.Token.ThrowIfCancellationRequested();
        return result ?? throw new InvalidOperationException("The scan produced no result.");
    }

    private static void DrawProgress(Screen s, ScanProgress p, string path, TimeSpan elapsed)
    {
        s.Clear();
        int w = s.Width;

        s.Fill(0, 0, w, ' ', Color.Header);
        s.Write(0, 0, Format.Fit($"windu {Version} ~ Use the arrow keys to navigate, press ? for help", w), Color.Header);

        // A centred box, matching ncdu's scanning window.
        var lines = new[]
        {
            "Scanning... " + FormatElapsed(elapsed),
            "",
            $"Total items: {Format.Count(Interlocked.Read(ref p.Items)),-10} size: {Format.Size(Interlocked.Read(ref p.TotalSize)).Trim()}",
            $"Current item: {Format.Fit(Format.Sanitize(p.Current), Math.Max(10, w - 22))}",
            "",
            "Press q to abort.",
        };

        int bw = Math.Min(w - 4, lines.Max(l => l.Length) + 4);
        int bh = lines.Length + 2;
        int bx = (w - bw) / 2, by = (s.Height - bh) / 2;
        if (bx < 0) bx = 0;
        if (by < 1) by = 1;

        for (int i = 0; i < bh; i++)
        {
            s.Fill(bx, by + i, bw, ' ', Color.Normal);
            s.Put(bx, by + i, '|', Color.Info);
            s.Put(bx + bw - 1, by + i, '|', Color.Info);
        }
        for (int i = 0; i < bw; i++)
        {
            s.Put(bx + i, by, '-', Color.Info);
            s.Put(bx + i, by + bh - 1, '-', Color.Info);
        }
        s.Put(bx, by, '+', Color.Info);
        s.Put(bx + bw - 1, by, '+', Color.Info);
        s.Put(bx, by + bh - 1, '+', Color.Info);
        s.Put(bx + bw - 1, by + bh - 1, '+', Color.Info);
        s.Write(bx + 2, by, " windu scan ", Color.Bold);

        for (int i = 0; i < lines.Length; i++)
            s.Write(bx + 2, by + 1 + i, Format.Fit(lines[i], bw - 4), i == 0 ? Color.Bold : Color.Normal);

        int fy = s.Height - 1;
        s.Fill(0, fy, w, ' ', Color.Header);
        s.Write(0, fy, Format.Fit(" Scanning " + path, w), Color.Header);

        s.Flush();
    }

    private static string FormatElapsed(TimeSpan t)
        => t.TotalMinutes >= 1
            ? $"{(int)t.TotalMinutes}m {t.Seconds}s"
            : $"{t.TotalSeconds:0.0}s";
}

/// <summary>Switches to the terminal's alternate screen buffer, like ncurses does.</summary>
internal static class AltScreen
{
    private static bool _active;

    public static void Enter()
    {
        if (_active) return;
        Console.Write("\x1b[?1049h\x1b[2J\x1b[H");
        Console.Out.Flush();
        _active = true;
    }

    public static void Leave()
    {
        if (!_active) return;
        Console.Write("\x1b[0m\x1b[?1049l");
        Console.Out.Flush();
        Console.CursorVisible = true;
        _active = false;
    }
}

/// <summary>Turns on ANSI escape processing for the console, which Windows disables by default.</summary>
internal static class VirtualTerminal
{
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    public static bool TryEnable()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            var h = GetStdHandle(STD_OUTPUT_HANDLE);
            if (h == IntPtr.Zero || h == new IntPtr(-1)) return Console.IsOutputRedirected;
            if (!GetConsoleMode(h, out var mode)) return Console.IsOutputRedirected;
            if ((mode & ENABLE_VIRTUAL_TERMINAL_PROCESSING) != 0) return true;
            return SetConsoleMode(h, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
        }
        catch { return false; }
    }
}
