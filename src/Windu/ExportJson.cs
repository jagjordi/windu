using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Windu;

/// <summary>
/// Reads and writes the ncdu export format (version 1.x), so trees produced here
/// can be opened by ncdu and vice versa.
///
/// The format is a JSON array: [1, 2, {metadata}, tree] where each directory is
/// itself an array whose first element is the directory's own info object and
/// whose remaining elements are its children.
/// </summary>
public static class ExportJson
{
    public static void Write(FsEntry root, string path, string rootPath)
    {
        using var w = path == "-"
            ? new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))
            : new StreamWriter(File.Create(path), new UTF8Encoding(false));

        w.Write("[1,2,{\"progname\":\"windu\",\"progver\":\"");
        w.Write(Program.Version);
        w.Write("\",\"timestamp\":");
        w.Write(DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        w.Write("},");
        WriteEntry(w, root, isRoot: true, rootPath);
        w.Write("]\n");
    }

    private static void WriteEntry(TextWriter w, FsEntry e, bool isRoot, string rootPath)
    {
        bool isDir = e.IsDirectory && !e.IsLink;
        if (isDir) w.Write('[');

        w.Write("{\"name\":");
        WriteJsonString(w, isRoot ? rootPath : e.Name);
        // ncdu records each entry's own size; subtree totals are recomputed on read.
        w.Write(",\"asize\":");
        w.Write(e.OwnSize.ToString(CultureInfo.InvariantCulture));
        w.Write(",\"dsize\":");
        w.Write(e.OwnDiskSize.ToString(CultureInfo.InvariantCulture));
        if (e.LastWriteTime != default)
        {
            w.Write(",\"mtime\":");
            w.Write(new DateTimeOffset(e.LastWriteTime.ToUniversalTime(), TimeSpan.Zero)
                .ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }
        if (e.ReadError) w.Write(",\"read_error\":true");
        if (e.IsLink) w.Write(",\"notreg\":true");
        if (e.IsDuplicate) w.Write(",\"hlnkc\":true");
        w.Write('}');

        if (isDir && e.Children is not null)
        {
            foreach (var c in e.Children)
            {
                w.Write(',');
                WriteEntry(w, c, isRoot: false, rootPath);
            }
        }

        if (isDir) w.Write(']');
    }

    private static void WriteJsonString(TextWriter w, string s)
    {
        w.Write('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':  w.Write("\\\""); break;
                case '\\': w.Write("\\\\"); break;
                case '\n': w.Write("\\n"); break;
                case '\r': w.Write("\\r"); break;
                case '\t': w.Write("\\t"); break;
                default:
                    if (c < 0x20) w.Write("\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else w.Write(c);
                    break;
            }
        }
        w.Write('"');
    }

    public static FsEntry Read(string path, out string rootName)
    {
        using var stream = path == "-" ? Console.OpenStandardInput() : File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });

        var arr = doc.RootElement;
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() < 4)
            throw new InvalidDataException("Not a recognised ncdu export file.");

        var tree = arr[3];
        var root = ParseEntry(tree, null);
        rootName = root.Name;
        return root;
    }

    private static FsEntry ParseEntry(JsonElement el, FsEntry? parent)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            // A directory: [info, child, child, ...]
            var dir = ParseInfo(el[0], parent, isDir: true);
            var children = new List<FsEntry>();
            dir.Children = children;

            long size = 0, disk = 0, count = 1;
            for (int i = 1; i < el.GetArrayLength(); i++)
            {
                var c = ParseEntry(el[i], dir);
                children.Add(c);
                size += c.Size;
                disk += c.DiskSize;
                count += c.ItemCount;
            }
            // The recorded sizes are the directory's own; add the children's totals.
            dir.Size = dir.OwnSize + size;
            dir.DiskSize = dir.OwnDiskSize + disk;
            dir.ItemCount = count;
            return dir;
        }

        return ParseInfo(el, parent, isDir: false);
    }

    private static FsEntry ParseInfo(JsonElement o, FsEntry? parent, bool isDir)
    {
        var e = new FsEntry
        {
            Parent = parent,
            IsDirectory = isDir,
            Name = o.TryGetProperty("name", out var n) ? (n.GetString() ?? "?") : "?",
        };
        if (o.TryGetProperty("asize", out var a) && a.TryGetInt64(out var av)) e.OwnSize = av;
        if (o.TryGetProperty("dsize", out var d) && d.TryGetInt64(out var dv)) e.OwnDiskSize = dv;
        e.Size = e.OwnSize;
        e.DiskSize = e.OwnDiskSize;
        if (o.TryGetProperty("mtime", out var m) && m.TryGetInt64(out var mv))
            e.LastWriteTime = DateTimeOffset.FromUnixTimeSeconds(mv).LocalDateTime;
        if (o.TryGetProperty("read_error", out var re) && re.ValueKind == JsonValueKind.True) e.ReadError = true;
        if (o.TryGetProperty("notreg", out var nr) && nr.ValueKind == JsonValueKind.True) e.IsLink = true;
        if (o.TryGetProperty("hlnkc", out var hl) && hl.ValueKind == JsonValueKind.True) e.IsDuplicate = true;
        return e;
    }
}
