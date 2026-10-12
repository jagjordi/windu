namespace Windu;

/// <summary>Command-line options, following ncdu's flag names where they apply.</summary>
public sealed class Options
{
    public string Path = ".";
    public string? ExportFile;
    public string? ImportFile;
    public bool Quiet;
    public bool ShowHelp;
    public bool ShowVersion;

    // Diagnostic: render the browser offscreen and print it as text (used by the tests).
    public string? RenderKeys;
    public int RenderWidth = 100;
    public int RenderHeight = 30;
    public readonly Settings Settings = new();
    public readonly ScanOptions ScanOptions = new();

    public static Options Parse(string[] args)
    {
        var o = new Options();
        bool pathSet = false;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];

            string Next(string flag)
            {
                if (i + 1 >= args.Length) throw new ArgumentException($"option '{flag}' requires an argument.");
                return args[++i];
            }

            switch (a)
            {
                case "-h": case "--help": o.ShowHelp = true; break;
                case "-v": case "-V": case "--version": o.ShowVersion = true; break;

                case "-o": o.ExportFile = Next(a); break;
                case "-f": o.ImportFile = Next(a); break;
                case "-q": case "--quiet": o.Quiet = true; break;

                case "--render": o.RenderKeys = Next(a); break;
                case "--render-size":
                {
                    var parts = Next(a).Split('x');
                    if (parts.Length != 2 || !int.TryParse(parts[0], out o.RenderWidth) || !int.TryParse(parts[1], out o.RenderHeight))
                        throw new ArgumentException("--render-size expects WIDTHxHEIGHT.");
                    break;
                }

                case "-x": o.ScanOptions.CrossFilesystems = false; break;
                case "-L": case "--follow-symlinks": o.ScanOptions.FollowLinks = true; break;
                case "--cross-file-system": o.ScanOptions.CrossFilesystems = true; break;
                case "--hard-link-detection": o.ScanOptions.CountHardLinksOnce = true; break;

                case "--exclude": o.ScanOptions.ExcludePatterns.Add(Next(a)); break;
                case "-X": case "--exclude-from":
                {
                    var file = Next(a);
                    foreach (var line in File.ReadAllLines(file))
                    {
                        var t = line.Trim();
                        if (t.Length > 0 && !t.StartsWith('#')) o.ScanOptions.ExcludePatterns.Add(t);
                    }
                    break;
                }

                case "--apparent-size": o.Settings.ShowApparentSize = true; break;
                case "--si": o.Settings.SiUnits = true; break;
                case "--dirs-first": o.Settings.DirsFirst = true; break;
                case "--show-percent": o.Settings.ShowPercent = true; break;
                case "--no-graph": o.Settings.ShowGraph = false; break;

                case "--sort":
                {
                    var v = Next(a).ToLowerInvariant();
                    o.Settings.Sort = v switch
                    {
                        "name" => SortField.Name,
                        "size" or "disk" => SortField.Size,
                        "count" or "items" => SortField.ItemCount,
                        "mtime" or "time" => SortField.MTime,
                        _ => throw new ArgumentException($"unknown sort field '{v}'."),
                    };
                    o.Settings.SortDescending = o.Settings.Sort != SortField.Name;
                    break;
                }

                default:
                    if (a.StartsWith('-') && a.Length > 1)
                        throw new ArgumentException($"unrecognised option '{a}'.");
                    if (pathSet) throw new ArgumentException("only one directory may be given.");
                    o.Path = a;
                    pathSet = true;
                    break;
            }
        }

        return o;
    }

    public static void PrintHelp()
    {
        Console.WriteLine($"""
            windu {Program.Version} - a disk usage analyzer for Windows, in the style of ncdu.

            Usage: windu [options] [directory]

            Modes:
              -o FILE                  Export the scan to FILE as ncdu-compatible JSON ('-' for stdout)
              -f FILE                  Open a previously exported file instead of scanning
              -q, --quiet              Do not draw the scan progress window

            Scanning:
              -x                       Stay on one filesystem (the default)
                  --cross-file-system  Descend into other volumes and mount points
              -L, --follow-symlinks    Follow symlinks and junctions
                  --exclude PATTERN    Skip names matching PATTERN (may be repeated)
              -X, --exclude-from FILE  Read exclude patterns from FILE, one per line
                  --hard-link-detection
                                       Count each hard link once instead of per name.
                                       Accurate but much slower: Windows exposes the
                                       link count only through an open file handle.

            Display:
                  --apparent-size      Start in apparent-size mode instead of disk usage
                  --si                 Use kB/MB (powers of 1000) instead of KiB/MiB
                  --dirs-first         List directories before files
                  --show-percent       Show the percentage column at startup
                  --no-graph           Hide the size graph at startup
                  --sort FIELD         Sort by name, size, count or mtime

            Other:
              -h, --help               Show this message
              -v, --version            Show the version

            Inside the browser, press ? for the list of keys.
            """);
    }
}
