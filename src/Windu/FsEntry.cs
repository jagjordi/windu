namespace Windu;

/// <summary>
/// A node in the scanned directory tree. Directories keep their children;
/// files keep only their own size. Sizes are aggregated bottom-up after a scan.
/// </summary>
public sealed class FsEntry
{
    public string Name = "";
    public FsEntry? Parent;
    public List<FsEntry>? Children;

    /// <summary>Apparent size: the logical length of the file(s).</summary>
    public long Size;

    /// <summary>Disk usage: size rounded up to the allocation unit (cluster).</summary>
    public long DiskSize;

    /// <summary>
    /// This entry's own size, excluding its children. For a file it equals
    /// <see cref="Size"/>; for a directory it is the size of the directory entry
    /// itself. The ncdu export format records these per-entry values, and the
    /// subtree totals above are recomputed from them on import.
    /// </summary>
    public long OwnSize;

    /// <summary>This entry's own disk usage, excluding its children.</summary>
    public long OwnDiskSize;

    /// <summary>Number of items in this subtree, including itself.</summary>
    public long ItemCount = 1;

    public bool IsDirectory;

    /// <summary>Set when the directory could not be fully read (access denied, etc.).</summary>
    public bool ReadError;

    /// <summary>A reparse point (symlink / junction / mount point) that we did not follow.</summary>
    public bool IsLink;

    /// <summary>A hard link whose blocks were already counted elsewhere in the tree.</summary>
    public bool IsDuplicate;

    public DateTime LastWriteTime;

    public bool HasError => ReadError || (Children?.Any(c => c.HasError) ?? false);

    public string FullPath
    {
        get
        {
            if (Parent is null) return Name;
            var stack = new Stack<string>();
            for (var e = this; e is not null && e.Parent is not null; e = e.Parent)
                stack.Push(e.Name);
            var root = this;
            while (root.Parent is not null) root = root.Parent;
            return Path.Combine(root.Name, Path.Combine(stack.ToArray()));
        }
    }

    public IEnumerable<FsEntry> Ancestors()
    {
        for (var e = Parent; e is not null; e = e.Parent) yield return e;
    }
}
