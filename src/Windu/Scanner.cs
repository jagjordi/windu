using System.IO.Enumeration;

namespace Windu;

public sealed class ScanProgress
{
    public long Items;
    public long TotalSize;
    public string Current = "";
}

public sealed class ScanOptions
{
    /// <summary>Cross filesystem/volume boundaries when recursing.</summary>
    public bool CrossFilesystems;

    /// <summary>Follow reparse points (symlinks, junctions). Off by default, like ncdu.</summary>
    public bool FollowLinks;

    /// <summary>
    /// Count hard-linked files only once per scan. Off by default: Windows exposes
    /// the link count only via an open handle, so enabling this costs one
    /// CreateFile per file and slows a large scan by roughly 40x.
    /// </summary>
    public bool CountHardLinksOnce;

    public List<string> ExcludePatterns = new();
}

/// <summary>
/// Recursive directory walker. Uses <see cref="FileSystemEnumerable{T}"/> so that
/// size and attributes come straight out of the directory enumeration buffer,
/// avoiding a separate stat syscall per entry.
/// </summary>
public sealed class Scanner
{
    private readonly ScanOptions _opts;
    private readonly ScanProgress _progress;
    private readonly CancellationToken _ct;
    private readonly uint _clusterSize;
    private readonly string _rootVolume;

    /// <summary>Levels below the scan root that are walked in parallel.</summary>
    private const int ParallelDepth = 2;

    // Tracks (volume, file id) of files with a link count > 1 that we have already
    // counted. Guarded by its own lock because the walk runs on several threads.
    private readonly HashSet<(uint, ulong)> _seenHardLinks = new();
    private readonly object _hardLinkLock = new();

    public Scanner(ScanOptions opts, ScanProgress progress, string rootPath, CancellationToken ct)
    {
        _opts = opts;
        _progress = progress;
        _ct = ct;
        _clusterSize = Native.GetClusterSize(rootPath);
        _rootVolume = Native.GetVolumeRoot(rootPath);
    }

    public FsEntry Scan(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (path.Length == 2 && path[1] == ':') path += Path.DirectorySeparatorChar;

        var root = new FsEntry
        {
            Name = path,
            IsDirectory = true,
        };
        ScanInto(root, path, 0);
        return root;
    }

    private void ScanInto(FsEntry dir, string path, int depth)
    {
        _ct.ThrowIfCancellationRequested();

        var children = new List<FsEntry>();
        dir.Children = children;
        var subdirs = new List<(FsEntry Entry, string Path)>();
        _progress.Current = path;

        FileSystemEnumerable<FsEntry?> enumerable;
        try
        {
            enumerable = new FileSystemEnumerable<FsEntry?>(
                path,
                (ref FileSystemEntry entry) => Convert(ref entry),
                new EnumerationOptions
                {
                    RecurseSubdirectories = false,
                    IgnoreInaccessible = true,
                    AttributesToSkip = 0,
                    ReturnSpecialDirectories = false,
                });
        }
        catch (Exception)
        {
            dir.ReadError = true;
            return;
        }

        IEnumerator<FsEntry?> it;
        try { it = enumerable.GetEnumerator(); }
        catch (Exception) { dir.ReadError = true; return; }

        using (it)
        {
            while (true)
            {
                FsEntry? child;
                try
                {
                    if (!it.MoveNext()) break;
                    child = it.Current;
                }
                catch (Exception)
                {
                    // A single unreadable entry must not abort the whole directory.
                    dir.ReadError = true;
                    continue;
                }

                if (child is null) continue;
                child.Parent = dir;
                children.Add(child);

                if (child.IsDirectory && !child.IsLink)
                {
                    var childPath = Path.Combine(path, child.Name);
                    if (!_opts.CrossFilesystems && !SameVolume(childPath))
                    {
                        child.Children = new List<FsEntry>();
                        continue;
                    }
                    subdirs.Add((child, childPath));
                }
            }
        }

        // Near the root the fan-out is wide enough to be worth spreading over
        // cores; deeper down the per-task overhead would outweigh the gain, and
        // unbounded nesting would swamp the thread pool.
        if (depth < ParallelDepth && subdirs.Count > 1)
        {
            Parallel.ForEach(
                subdirs,
                new ParallelOptions { CancellationToken = _ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                sd => ScanInto(sd.Entry, sd.Path, depth + 1));
        }
        else
        {
            foreach (var sd in subdirs) ScanInto(sd.Entry, sd.Path, depth + 1);
        }

        // Aggregate this directory's totals from its children.
        long size = 0, disk = 0, count = 1;
        foreach (var c in children)
        {
            size += c.Size;
            disk += c.DiskSize;
            count += c.ItemCount;
        }
        // The directory entry itself occupies at least one cluster.
        dir.OwnSize = 0;
        dir.OwnDiskSize = _clusterSize;
        dir.Size = size + dir.OwnSize;
        dir.DiskSize = disk + dir.OwnDiskSize;
        dir.ItemCount = count;

        Interlocked.Add(ref _progress.Items, children.Count);
        Interlocked.Add(ref _progress.TotalSize, size);
    }

    private FsEntry? Convert(ref FileSystemEntry entry)
    {
        var name = entry.FileName.ToString();

        if (_opts.ExcludePatterns.Count > 0 && IsExcluded(name))
            return null;

        var attrs = entry.Attributes;
        bool isLink = (attrs & FileAttributes.ReparsePoint) != 0 && !_opts.FollowLinks;
        bool isDir = entry.IsDirectory;

        var e = new FsEntry
        {
            Name = name,
            IsDirectory = isDir,
            IsLink = isLink,
            LastWriteTime = entry.LastWriteTimeUtc.LocalDateTime,
        };

        if (isDir)
        {
            // Sizes are filled in by ScanInto; a link is a leaf and stays at zero.
            e.Size = 0;
            e.DiskSize = isLink ? _clusterSize : 0;
            e.OwnSize = 0;
            e.OwnDiskSize = e.DiskSize;
            return e;
        }

        long length = entry.Length;
        e.Size = length;
        e.DiskSize = RoundToCluster(length);
        e.OwnSize = e.Size;
        e.OwnDiskSize = e.DiskSize;

        if (isLink)
        {
            // Do not attribute the target's blocks to the link itself.
            e.DiskSize = _clusterSize;
            e.OwnDiskSize = e.DiskSize;
            return e;
        }

        if (_opts.CountHardLinksOnce)
        {
            var full = Path.Combine(entry.Directory.ToString(), name);
            if (Native.TryGetFileId(full, out var vol, out var id, out var links) && links > 1)
            {
                bool firstSighting;
                lock (_hardLinkLock) firstSighting = _seenHardLinks.Add((vol, id));
                if (!firstSighting)
                {
                    e.IsDuplicate = true;
                    e.Size = 0;
                    e.DiskSize = 0;
                    e.OwnSize = 0;
                    e.OwnDiskSize = 0;
                }
            }
        }

        return e;
    }

    private bool IsExcluded(string name)
    {
        foreach (var pat in _opts.ExcludePatterns)
            if (Glob.IsMatch(name, pat)) return true;
        return false;
    }

    private long RoundToCluster(long length)
    {
        if (_clusterSize == 0) return length;
        var cs = (long)_clusterSize;
        return (length + cs - 1) / cs * cs;
    }

    private bool SameVolume(string path)
    {
        try { return string.Equals(Native.GetVolumeRoot(path), _rootVolume, StringComparison.OrdinalIgnoreCase); }
        catch { return true; }
    }
}
