using System.Text.Json;
using Windu;
using Xunit;

namespace Windu.Tests;

public class ExportTests
{
    private static FsEntry Scan(string path)
        => new Scanner(new ScanOptions(), new ScanProgress(), path, CancellationToken.None).Scan(path);

    [Fact]
    public void Export_produces_the_ncdu_envelope()
    {
        using var t = new TempTree();
        t.File("a.bin", 42);
        var root = Scan(t.Root);

        var file = Path.Combine(t.Root, "..", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ExportJson.Write(root, file, root.Name);
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var arr = doc.RootElement;

            Assert.Equal(JsonValueKind.Array, arr.ValueKind);
            Assert.Equal(1, arr[0].GetInt32());          // major version
            Assert.Equal(2, arr[1].GetInt32());          // minor version
            Assert.Equal("windu", arr[2].GetProperty("progname").GetString());
            Assert.Equal(JsonValueKind.Array, arr[3].ValueKind);   // the tree
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Round_trip_preserves_sizes_and_counts()
    {
        using var t = new TempTree();
        t.File("a.bin", 1000);
        t.File("sub/b.bin", 2000);
        t.File("sub/deep/c.bin", 3000);
        var original = Scan(t.Root);

        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ExportJson.Write(original, file, original.Name);
            var reloaded = ExportJson.Read(file, out _);

            Assert.Equal(original.Size, reloaded.Size);
            Assert.Equal(original.DiskSize, reloaded.DiskSize);
            Assert.Equal(original.ItemCount, reloaded.ItemCount);
            Assert.Equal(original.Children!.Count, reloaded.Children!.Count);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Round_trip_is_byte_identical_when_repeated()
    {
        using var t = new TempTree();
        t.File("a.bin", 10);
        t.File("sub/b.bin", 20);
        var root = Scan(t.Root);

        var f1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        var f2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ExportJson.Write(root, f1, root.Name);
            var reloaded = ExportJson.Read(f1, out _);
            ExportJson.Write(reloaded, f2, reloaded.Name);

            // Only the embedded timestamp may differ, so compare the tree portion.
            var a = File.ReadAllText(f1);
            var b = File.ReadAllText(f2);
            Assert.Equal(a[a.IndexOf("},", StringComparison.Ordinal)..],
                         b[b.IndexOf("},", StringComparison.Ordinal)..]);
        }
        finally { File.Delete(f1); File.Delete(f2); }
    }

    [Fact]
    public void Names_with_quotes_and_backslashes_are_escaped()
    {
        var root = new FsEntry { Name = @"C:\root", IsDirectory = true, Children = new List<FsEntry>() };
        var child = new FsEntry { Name = "od\"d\\name", Parent = root, Size = 1, OwnSize = 1 };
        root.Children.Add(child);

        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ExportJson.Write(root, file, root.Name);
            var reloaded = ExportJson.Read(file, out _);
            Assert.Equal("od\"d\\name", reloaded.Children![0].Name);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Reading_a_non_export_file_reports_a_clear_error()
    {
        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, "{\"not\":\"an export\"}");
            Assert.Throws<InvalidDataException>(() => ExportJson.Read(file, out _));
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void Directory_entries_record_their_own_size_not_the_subtree_total()
    {
        using var t = new TempTree();
        t.File("sub/big.bin", 500_000);
        var root = Scan(t.Root);

        var file = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            ExportJson.Write(root, file, root.Name);
            using var doc = JsonDocument.Parse(File.ReadAllText(file));

            // arr[3] is the root directory array; its [0] is the root's own info.
            var rootInfo = doc.RootElement[3][0];
            Assert.Equal(0, rootInfo.GetProperty("asize").GetInt64());
        }
        finally { File.Delete(file); }
    }
}
