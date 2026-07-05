namespace UnicodeRegEx.Tools.Engine
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Runtime.InteropServices;
    using Microsoft.Win32.SafeHandles;

    /// <summary>
    /// A single entry produced by <see cref="NativeDir.Enumerate(string)"/>. Carries only
    /// the fields the directory walk needs, projected out of the native <c>WIN32_FIND_DATA</c> so callers
    /// never touch the marshalled structure.
    /// </summary>
    internal readonly struct NativeDirEntry
    {
        public NativeDirEntry(string name, FileAttributes attributes, uint reparseTag)
        {
            this.Name = name;
            this.Attributes = attributes;
            this.ReparseTag = reparseTag;
        }

        /// <summary>The entry's file name (never <c>.</c> or <c>..</c>; those are filtered out).</summary>
        public string Name { get; }

        /// <summary>The entry's attributes (<c>dwFileAttributes</c>).</summary>
        public FileAttributes Attributes { get; }

        /// <summary>
        /// The reparse tag (<c>dwReserved0</c>), meaningful only when
        /// <see cref="Attributes"/> has <see cref="FileAttributes.ReparsePoint"/> set; otherwise 0.
        /// </summary>
        public uint ReparseTag { get; }

        /// <summary>True if this entry is a directory.</summary>
        public bool IsDirectory => (this.Attributes & FileAttributes.Directory) != 0;

        /// <summary>True if this entry is a reparse point (a symlink/junction/cloud/dedup/etc. placeholder).</summary>
        public bool IsReparsePoint => (this.Attributes & FileAttributes.ReparsePoint) != 0;

        /// <summary>
        /// True if this entry is a reparse point whose tag is a *name surrogate* (a real link the walk
        /// should treat as a link: symlink, junction, mount point). False for reparse points that are not
        /// links (cloud placeholders, Data Dedup stubs, ProjFS, container isolation), which are ordinary
        /// directories/files that merely happen to use a reparse point.
        /// </summary>
        public bool IsNameSurrogate =>
            this.IsReparsePoint && NativeDir.IsNameSurrogateTag(this.ReparseTag);
    }

    /// <summary>
    /// A durable identity for a directory: its volume serial number plus its 128-bit file id. Two paths
    /// that resolve to the same directory (e.g. one reached through a junction) share the same
    /// <see cref="DirectoryId"/>, which is how the walk detects link cycles without relying on paths.
    /// </summary>
    internal readonly struct DirectoryId : IEquatable<DirectoryId>
    {
        private readonly ulong volumeSerialNumber;
        private readonly ulong fileIdLow;
        private readonly ulong fileIdHigh;

        public DirectoryId(ulong volumeSerialNumber, ulong fileIdLow, ulong fileIdHigh)
        {
            this.volumeSerialNumber = volumeSerialNumber;
            this.fileIdLow = fileIdLow;
            this.fileIdHigh = fileIdHigh;
        }

        public bool Equals(DirectoryId other) =>
            this.volumeSerialNumber == other.volumeSerialNumber &&
            this.fileIdLow == other.fileIdLow &&
            this.fileIdHigh == other.fileIdHigh;

        public override bool Equals(object? obj) => obj is DirectoryId other && this.Equals(other);

        public override int GetHashCode()
        {
            // Simple FNV-ish combine; the id components are already well-distributed.
            var hash = this.volumeSerialNumber;
            hash = (hash * 31) ^ this.fileIdLow;
            hash = (hash * 31) ^ this.fileIdHigh;
            return hash.GetHashCode();
        }
    }

    /// <summary>
    /// A thin, allocation-light wrapper over the Win32 <c>FindFirstFileEx</c>/<c>FindNextFile</c> loop that
    /// yields <see cref="NativeDirEntry"/> values. Unlike <see cref="FileSystemInfo"/>, this surfaces the
    /// reparse <b>tag</b> (from <c>dwReserved0</c>), which the BCL does not expose on netstandard2.0/net48,
    /// so the caller can distinguish real links from non-link reparse points.
    /// </summary>
    internal static class NativeDir
    {
        // Bit 0x20000000 of a reparse tag marks a "name surrogate" (see IsReparseTagNameSurrogate in
        // ntifs.h). Name-surrogate tags represent links (symlink/junction/mount point); non-surrogate tags
        // (cloud, dedup, ProjFS, WCI, ...) are placeholders that should be walked like normal directories.
        private const uint ReparseTagNameSurrogateBit = 0x20000000;

        /// <summary>
        /// Returns true if <paramref name="reparseTag"/> is a name-surrogate tag (a link).
        /// Mirrors the Win32 <c>IsReparseTagNameSurrogate</c> macro.
        /// </summary>
        public static bool IsNameSurrogateTag(uint reparseTag) => (reparseTag & ReparseTagNameSurrogateBit) != 0;

        /// <summary>
        /// Attempts to read the durable <see cref="DirectoryId"/> (volume serial + 128-bit file id) of the
        /// directory at <paramref name="path"/>. Used for link-cycle detection: two paths that resolve to the
        /// same directory share an id.
        /// </summary>
        /// <param name="path">The directory path to identify.</param>
        /// <param name="id">On success, the directory's identity.</param>
        /// <returns>
        /// True if the id was obtained. False if the directory could not be opened, or the volume does not
        /// provide a 128-bit file id (older/uncommon file systems). Callers treat false as "identity unknown".
        /// </returns>
        public static bool TryGetDirectoryId(string path, out DirectoryId id)
        {
            // FILE_FLAG_BACKUP_SEMANTICS is required to obtain a handle to a *directory*. Requesting no access
            // (0 / dwDesiredAccess) with full sharing lets this succeed even while others use the directory,
            // and needs no read permission on the contents. OPEN_EXISTING because the directory must exist.
            using var handle = NativeMethods.CreateFile(
                ToExtendedLengthPath(path),
                0,
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE | NativeMethods.FILE_SHARE_DELETE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                NativeMethods.FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                id = default;
                return false;
            }

            if (!NativeMethods.GetFileInformationByHandleEx(
                    handle,
                    NativeMethods.FILE_INFO_BY_HANDLE_CLASS.FileIdInfo,
                    out var info,
                    (uint)Marshal.SizeOf<NativeMethods.FILE_ID_INFO>()))
            {
                id = default;
                return false;
            }

            id = new DirectoryId(info.VolumeSerialNumber, info.FileIdLow, info.FileIdHigh);
            return true;
        }

        /// <summary>
        /// Enumerates the immediate children of <paramref name="directory"/> (not recursive), yielding one
        /// <see cref="NativeDirEntry"/> per child. The <c>.</c> and <c>..</c> pseudo-entries are skipped.
        /// </summary>
        /// <param name="directory">The directory whose children to enumerate.</param>
        /// <param name="skipDirectories">
        /// When true, directory entries are skipped before their name is materialized. Because
        /// <c>WIN32_FIND_DATA.cFileName</c> is a blittable fixed buffer (not an auto-marshalled string), a
        /// skipped directory allocates nothing: only <c>dwFileAttributes</c> (a plain field) is read, and the
        /// name <see cref="string"/> is built solely for entries that are actually yielded. This mirrors the
        /// BCL's <c>EnumerateFiles</c> fast path (which is fast precisely because it never turns skipped
        /// entries into objects).
        /// </param>
        /// <returns>A lazily evaluated sequence of the directory's immediate children.</returns>
        /// <exception cref="Win32Exception">
        /// Thrown if the directory cannot be opened or a native enumeration call fails for a reason other
        /// than "no more files". An empty directory (and a directory that yields
        /// <c>ERROR_FILE_NOT_FOUND</c>/<c>ERROR_NO_MORE_FILES</c>) completes without throwing.
        /// </exception>
        public static IEnumerable<NativeDirEntry> Enumerate(string directory, bool skipDirectories = false)
        {
            if (directory == null)
            {
                throw new ArgumentNullException(nameof(directory));
            }

            // FindFirstFile takes a search pattern; "<dir>\*" matches every child. The path is normalized to
            // an extended-length ("\\?\") form so paths longer than MAX_PATH work, matching the BCL walk.
            var searchPattern = Path.Combine(ToExtendedLengthPath(directory), "*");

            using var handle = NativeMethods.FindFirstFileEx(
                searchPattern,
                NativeMethods.FINDEX_INFO_LEVELS.FindExInfoBasic, // Skip the 8.3 alternate-name fill (faster).
                out var findData,
                NativeMethods.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                NativeMethods.FIND_FIRST_EX_LARGE_FETCH);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == NativeMethods.ERROR_FILE_NOT_FOUND || error == NativeMethods.ERROR_NO_MORE_FILES)
                {
                    // An empty directory: nothing to yield, not an error.
                    yield break;
                }

                throw new Win32Exception(error);
            }

            do
            {
                var attributes = (FileAttributes)findData.dwFileAttributes;

                // Gate on the cheap scalar field before touching the name. When skipping directories, a
                // directory entry costs one field read and a branch -- no string, no NativeDirEntry.
                if (skipDirectories && (attributes & FileAttributes.Directory) != 0)
                {
                    continue;
                }

                // "." and ".." are read straight from the fixed buffer so they never allocate a string.
                if (IsDotOrDotDot(in findData))
                {
                    continue;
                }

                yield return new NativeDirEntry(
                    GetFileName(in findData),
                    attributes,
                    findData.dwReserved0);
            }
            while (FindNext(handle, out findData));
        }

        // Advances the enumeration; returns false at the end (ERROR_NO_MORE_FILES) and rethrows any other
        // failure so the caller can report it (matching the whole-directory error handling in the walk).
        private static bool FindNext(SafeFindHandle handle, out NativeMethods.WIN32_FIND_DATA findData)
        {
            if (NativeMethods.FindNextFile(handle, out findData))
            {
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == NativeMethods.ERROR_NO_MORE_FILES)
            {
                return false;
            }

            throw new Win32Exception(error);
        }

        // Reads the NUL-terminated cFileName fixed buffer into a managed string. Called only for entries
        // that are actually yielded, so skipped directories / "."/".." never allocate a name.
        private static unsafe string GetFileName(in NativeMethods.WIN32_FIND_DATA findData)
        {
            fixed (char* name = findData.cFileName)
            {
                int length = 0;
                while (length < NativeMethods.MaxPathChars && name[length] != '\0')
                {
                    length++;
                }

                return new string(name, 0, length);
            }
        }

        // Detects the "." and ".." pseudo-entries directly from the fixed buffer, without materializing a
        // string (they are always skipped, so allocating their names would be pure waste).
        private static unsafe bool IsDotOrDotDot(in NativeMethods.WIN32_FIND_DATA findData)
        {
            fixed (char* name = findData.cFileName)
            {
                return name[0] == '.' &&
                    (name[1] == '\0' || (name[1] == '.' && name[2] == '\0'));
            }
        }

        // Prefixes the path with the extended-length marker so FindFirstFileEx is not bound by MAX_PATH.
        // Already-extended paths (\\?\ or \\?\UNC\) and non-rooted paths are left untouched.
        private static string ToExtendedLengthPath(string path)
        {
            const string Prefix = @"\\?\";
            const string UncPrefix = @"\\?\UNC\";

            if (path.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return path;
            }

            // Only rooted, non-relative paths can be safely prefixed. Leave anything else as-is; the caller
            // passes DirectoryInfo.FullName (always rooted) in practice, but stay defensive.
            if (!Path.IsPathRooted(path))
            {
                return path;
            }

            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                // UNC share: \\server\share -> \\?\UNC\server\share
                return UncPrefix + path.Substring(2);
            }

            return Prefix + path;
        }

        private static class NativeMethods
        {
            public const int ERROR_FILE_NOT_FOUND = 2;
            public const int ERROR_NO_MORE_FILES = 18;

            // Length of the cFileName fixed buffer (MAX_PATH), used to bound the NUL scan.
            public const int MaxPathChars = 260;

            // FindFirstFileEx dwAdditionalFlags: use a larger buffer for the directory scan (faster).
            public const int FIND_FIRST_EX_LARGE_FETCH = 0x2;

            public enum FINDEX_INFO_LEVELS
            {
                FindExInfoStandard = 0,
                FindExInfoBasic = 1, // Does not populate cAlternateFileName (the 8.3 name).
            }

            public enum FINDEX_SEARCH_OPS
            {
                FindExSearchNameMatch = 0,
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern SafeFindHandle FindFirstFileEx(
                string lpFileName,
                FINDEX_INFO_LEVELS fInfoLevelId,
                out WIN32_FIND_DATA lpFindFileData,
                FINDEX_SEARCH_OPS fSearchOp,
                IntPtr lpSearchFilter,
                int dwAdditionalFlags);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool FindNextFile(SafeFindHandle hFindFile, out WIN32_FIND_DATA lpFindFileData);

            // CreateFile share modes and flags used to open a directory handle for identity (FileIdInfo).
            public const uint FILE_SHARE_READ = 0x1;
            public const uint FILE_SHARE_WRITE = 0x2;
            public const uint FILE_SHARE_DELETE = 0x4;
            public const uint OPEN_EXISTING = 3;

            // Required to open a handle to a directory (rather than a file) with CreateFile.
            public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

            public enum FILE_INFO_BY_HANDLE_CLASS
            {
                FileIdInfo = 18, // Yields FILE_ID_INFO (volume serial + 128-bit file id).
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern SafeFileHandle CreateFile(
                string lpFileName,
                uint dwDesiredAccess,
                uint dwShareMode,
                IntPtr lpSecurityAttributes,
                uint dwCreationDisposition,
                uint dwFlagsAndAttributes,
                IntPtr hTemplateFile);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetFileInformationByHandleEx(
                SafeFileHandle hFile,
                FILE_INFO_BY_HANDLE_CLASS fileInformationClass,
                out FILE_ID_INFO lpFileInformation,
                uint dwBufferSize);

            // FILE_ID_INFO: a volume serial number plus a 128-bit file id (FILE_ID_128, two 64-bit halves).
            // The 128-bit id is ReFS-correct; on NTFS the high half is zero and the low half is the file index.
            [StructLayout(LayoutKind.Sequential)]
            public struct FILE_ID_INFO
            {
                public ulong VolumeSerialNumber;
                public ulong FileIdLow;
                public ulong FileIdHigh;
            }

            // Blittable layout: cFileName/cAlternateFileName are fixed char buffers rather than
            // [MarshalAs(ByValTStr)] strings, so the runtime does NOT allocate a string per entry when the
            // struct is marshalled back. The name string is built on demand (see GetFileName) only for
            // entries the caller keeps. The whole struct is blittable, so out-marshalling is a raw copy.
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public unsafe struct WIN32_FIND_DATA
            {
                public uint dwFileAttributes;
                public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
                public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
                public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
                public uint nFileSizeHigh;
                public uint nFileSizeLow;

                // For a reparse point, dwReserved0 holds the reparse tag; otherwise it is unused.
                public uint dwReserved0;
                public uint dwReserved1;

                public fixed char cFileName[MaxPathChars];
                public fixed char cAlternateFileName[14];
            }
        }
    }
}
