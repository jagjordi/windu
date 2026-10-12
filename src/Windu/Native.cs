using System.Runtime.InteropServices;

namespace Windu;

internal static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceW(
        string lpRootPathName,
        out uint lpSectorsPerCluster,
        out uint lpBytesPerSector,
        out uint lpNumberOfFreeClusters,
        out uint lpTotalNumberOfClusters);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandleWrapper CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandleWrapper hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public long ftCreationTime;
        public long ftLastAccessTime;
        public long ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    internal sealed class SafeFileHandleWrapper : SafeHandle
    {
        public SafeFileHandleWrapper() : base(IntPtr.Zero, true) { }
        public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;
    private const uint OPEN_EXISTING = 3;

    private static readonly Dictionary<string, uint> ClusterCache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetVolumeRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        return string.IsNullOrEmpty(root) ? full : root;
    }

    /// <summary>Allocation unit size in bytes, or 0 if it cannot be determined.</summary>
    public static uint GetClusterSize(string path)
    {
        var root = GetVolumeRoot(path);
        lock (ClusterCache)
        {
            if (ClusterCache.TryGetValue(root, out var cached)) return cached;
            uint result = 4096;
            try
            {
                if (GetDiskFreeSpaceW(root, out var spc, out var bps, out _, out _))
                    result = spc * bps;
            }
            catch { /* keep the 4 KiB default */ }
            ClusterCache[root] = result;
            return result;
        }
    }

    public static bool TryGetDiskSpace(string path, out ulong total, out ulong free)
    {
        total = 0; free = 0;
        try
        {
            if (!GetDiskFreeSpaceExW(GetVolumeRoot(path), out _, out var t, out var f)) return false;
            total = t; free = f;
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Reads the volume serial and file index that together identify a file uniquely,
    /// plus its hard link count. Used to avoid double-counting hard links.
    /// </summary>
    public static bool TryGetFileId(string path, out uint volumeSerial, out ulong fileId, out uint linkCount)
    {
        volumeSerial = 0; fileId = 0; linkCount = 1;
        try
        {
            using var h = CreateFileW(Prefix(path), 0, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
            if (h.IsInvalid) return false;
            if (!GetFileInformationByHandle(h, out var info)) return false;
            volumeSerial = info.dwVolumeSerialNumber;
            fileId = ((ulong)info.nFileIndexHigh << 32) | info.nFileIndexLow;
            linkCount = info.nNumberOfLinks;
            return true;
        }
        catch { return false; }
    }

    /// <summary>Adds the extended-length prefix so paths longer than MAX_PATH work.</summary>
    public static string Prefix(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path.Substring(2);
        return Path.IsPathFullyQualified(path) ? @"\\?\" + path : path;
    }
}
