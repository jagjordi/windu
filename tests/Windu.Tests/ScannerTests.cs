using Windu;
using Xunit;

namespace Windu.Tests;

/// <summary>Builds a throwaway directory tree on disk and removes it afterwards.</summary>
public sealed class TempTree : IDisposable
{
    public string Root { get; }

    public TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "windu-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Dir(string relative)
    {
        var p = Path.Combine(Root, relative);
        Directory.CreateDirectory(p);
        return p;
    }

    public string File(string relative, long bytes)
    {
        var p = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        using var fs = System.IO.File.Create(p);
        fs.SetLength(bytes);
        return p;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { /* best effort */ }
    }
}

public class ScannerTests
{
    private static FsEntry Scan(string path, ScanOptions? opts = null)
    {
        var scanner = new Scanner(opts ?? new ScanOptions(), new ScanProgress(), path, CancellationToken.None);
        return scanner.Scan(path);
    }

    private static FsEntry Child(FsEntry dir, string name)
        => dir.Children!.Single(c => c.Name == name);

    [Fact]
    public void Apparent_size_is_the_exact_sum_of_file_lengths()
    {
        using var t = new TempTree();
        t.File("a.bin", 1000);
        t.File("sub/b.bin", 2500);
        t.File("sub/deep/c.bin", 7);

        var root = Scan(t.Root);

        Assert.Equal(1000 + 2500 + 7, root.Size);
    }

    [Fact]
    public void Disk_size_rounds_each_file_up_to_a_cluster()
    {
        using var t = new TempTree();
        t.File("tiny.bin", 1);

        var root = Scan(t.Root);
        var tiny = Child(root, "tiny.bin");

        // A one-byte file still consumes a whole allocation unit.
        Assert.True(tiny.DiskSize >= 512, $"expected at least one cluster, got {tiny.DiskSize}");
        Assert.Equal(0, tiny.DiskSize % 512);
        Assert.True(tiny.DiskSize > tiny.Size);
    }

    [Fact]
    public void Empty_file_has_zero_apparent_size()
    {
        using var t = new TempTree();
        t.File("empty.bin", 0);

        var root = Scan(t.Root);
        Assert.Equal(0, Child(root, "empty.bin").Size);
    }

    [Fact]
    public void Item_count_covers_the_whole_subtree()
    {
        using var t = new TempTree();
        t.File("a.bin", 1);
        t.File("sub/b.bin", 1);
        t.File("sub/c.bin", 1);

        var root = Scan(t.Root);

        // a.bin, sub, sub/b.bin, sub/c.bin, plus the root itself.
        Assert.Equal(5, root.ItemCount);
    }

    [Fact]
    public void Sizes_aggregate_up_through_nested_directories()
    {
        using var t = new TempTree();
        t.File("sub/deep/deeper/x.bin", 4096);

        var root = Scan(t.Root);
        var sub = Child(root, "sub");
        var deep = Child(sub, "deep");
        var deeper = Child(deep, "deeper");

        Assert.Equal(4096, deeper.Size);
        Assert.Equal(4096, deep.Size);
        Assert.Equal(4096, sub.Size);
        Assert.Equal(4096, root.Size);
    }

    [Fact]
    public void Empty_directory_is_recorded_with_no_children()
    {
        using var t = new TempTree();
        t.Dir("hollow");

        var root = Scan(t.Root);
        var hollow = Child(root, "hollow");

        Assert.True(hollow.IsDirectory);
        Assert.Empty(hollow.Children!);
        Assert.Equal(0, hollow.Size);
    }

    [Fact]
    public void Exclude_patterns_skip_matching_entries()
    {
        using var t = new TempTree();
        t.File("keep.txt", 100);
        t.File("node_modules/junk.bin", 999999);

        var opts = new ScanOptions();
        opts.ExcludePatterns.Add("node_modules");
        var root = Scan(t.Root, opts);

        Assert.Single(root.Children!);
        Assert.Equal("keep.txt", root.Children![0].Name);
        Assert.Equal(100, root.Size);
    }

    [Fact]
    public void Exclude_patterns_accept_wildcards()
    {
        using var t = new TempTree();
        t.File("a.log", 10);
        t.File("b.txt", 20);

        var opts = new ScanOptions();
        opts.ExcludePatterns.Add("*.log");
        var root = Scan(t.Root, opts);

        Assert.Single(root.Children!);
        Assert.Equal("b.txt", root.Children![0].Name);
    }

    [Fact]
    public void Own_size_excludes_children_so_exports_round_trip()
    {
        using var t = new TempTree();
        t.File("sub/a.bin", 5000);

        var root = Scan(t.Root);
        var sub = Child(root, "sub");

        // The directory's own apparent size is zero; the subtree total is not.
        Assert.Equal(0, sub.OwnSize);
        Assert.Equal(5000, sub.Size);
    }

    [Fact]
    public void Parent_links_let_an_entry_rebuild_its_path()
    {
        using var t = new TempTree();
        t.File("sub/a.bin", 1);

        var root = Scan(t.Root);
        var file = Child(Child(root, "sub"), "a.bin");

        Assert.Equal(Path.Combine(t.Root, "sub", "a.bin"), file.FullPath);
    }

    [Fact]
    public void Cancellation_stops_the_scan()
    {
        using var t = new TempTree();
        for (int i = 0; i < 50; i++) t.File($"d{i}/f.bin", 10);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var scanner = new Scanner(new ScanOptions(), new ScanProgress(), t.Root, cts.Token);

        Assert.Throws<OperationCanceledException>(() => scanner.Scan(t.Root));
    }

    [Fact]
    public void Progress_counters_advance_during_a_scan()
    {
        using var t = new TempTree();
        t.File("a.bin", 1234);
        t.File("sub/b.bin", 4321);

        var progress = new ScanProgress();
        new Scanner(new ScanOptions(), progress, t.Root, CancellationToken.None).Scan(t.Root);

        Assert.True(progress.Items >= 3, $"expected at least 3 items, saw {progress.Items}");
        Assert.True(progress.TotalSize >= 1234 + 4321);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Unreadable_directory_is_flagged_rather_than_aborting_the_scan()
    {
        using var t = new TempTree();
        t.File("readable.bin", 100);
        var locked = t.Dir("locked");

        // Deny listing to everyone, so enumeration of this one directory fails.
        var info = new DirectoryInfo(locked);
        var sec = info.GetAccessControl();
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        sec.AddAccessRule(rule);
        try
        {
            info.SetAccessControl(sec);

            var root = Scan(t.Root);

            // The readable part must still be present and correct.
            Assert.Equal(100, Child(root, "readable.bin").Size);
        }
        finally
        {
            sec.RemoveAccessRule(rule);
            info.SetAccessControl(sec);
        }
    }
}
