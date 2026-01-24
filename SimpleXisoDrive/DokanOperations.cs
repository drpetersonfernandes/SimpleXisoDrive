using System.Security.AccessControl;
using DokanNet;
using DokanNet.Logging;
using SimpleXisoDrive.Services;
using SimpleXisoDrive.XDVDFs;
using FileAccess = DokanNet.FileAccess;

namespace SimpleXisoDrive;

public class XboxIsoVfsDokan(VfsContainer vfs) : IDokanOperations
{
    private readonly VfsContainer _vfs = vfs;
    private static readonly ConsoleLogger Logger = new("[VFS] ");

    public NtStatus CreateFile(string fileName, FileAccess access, FileShare share,
        FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
    {
        LogOperation();

        var normalizedPath = fileName.Replace('/', '\\');

        // Handle special paths "." and ".."
        // If path is "\." or "\..", treat as root "\"
        if (normalizedPath is @"\." or @"\..")
        {
            normalizedPath = @"\";
        }
        // If path is "\SomeDir\." or "\SomeDir\..", resolve to the actual directory or its parent
        else if (normalizedPath.EndsWith(@"\.", StringComparison.Ordinal) || normalizedPath.EndsWith(@"\..", StringComparison.Ordinal))
        {
            var parentOfSpecial = Path.GetDirectoryName(normalizedPath); // Gets "\SomeDir"
            if (parentOfSpecial == null) // Should not happen for non-root special paths
            {
                return DokanResult.PathNotFound;
            }

            if (normalizedPath.EndsWith(@"\.", StringComparison.Ordinal))
            {
                normalizedPath = parentOfSpecial; // Resolve "\SomeDir\." to "\SomeDir"
            }
            else // EndsWith("\..")
            {
                // Resolve "\SomeDir\.." to the parent of "\SomeDir"
                normalizedPath = Path.GetDirectoryName(parentOfSpecial) ?? @"\"; // Parent of "\SomeDir" is "\"
            }
        }


        // Always allow directory creation requests (for the root or existing directories)
        // This is often a check for directory existence.
        if (normalizedPath == @"\")
        {
            // Check if the root entry is valid (it should be cached)
            var rootEntry = _vfs.GetEntry(@"\");
            if (rootEntry is not { IsDirectory: true })
            {
                 // This indicates a fundamental problem if root isn't found/valid
                 Logger.Error("Root entry not found or not a directory during CreateFile for root.");
                 return DokanResult.Error; // Or some other appropriate error
            }

            info.IsDirectory = true;
            info.Context = rootEntry; // Optionally set context for root, though not strictly needed by other ops usually
            return DokanResult.Success;
        }

        var entry = _vfs.GetEntry(normalizedPath);

        if (entry == null)
        {
            // If the entry is not found in the VFS, it doesn't exist.
            // Dokan might call CreateFile with FileMode.Open to check for existence.
            // If it's not FileMode.Open, it might be trying to create something, which we don't support.
            if (mode != FileMode.Open)
            {
                return DokanResult.AccessDenied; // Read-only file system
            }

            // If it's FileMode.Open and not found, it's simply FileNotFound.
            return DokanResult.FileNotFound;
        }

        // Entry found
        info.IsDirectory = entry.IsDirectory;
        info.Context = entry; // Store the FileEntry in the context for later operations

        // Check if the requested access is compatible with a read-only file system
        // If any write access is requested, deny it.
        // Correct flags from DokanNet.FileAccess are used here.
        if ((access & FileAccess.GenericWrite) != 0 ||
            (access & FileAccess.WriteData) != 0 ||
            (access & FileAccess.AppendData) != 0 ||
            (access & FileAccess.Delete) != 0 || // Also deny delete access
            (access & FileAccess.DeleteChild) != 0 || // Deny deleting children in a directory
            (access & FileAccess.WriteAttributes) != 0 || // Deny changing attributes
            (access & FileAccess.WriteExtendedAttributes) != 0) // Deny changing extended attributes
        {
            return DokanResult.AccessDenied;
        }

        return mode switch
        {
            // For FileMode.CreateNew, if the file already exists, return AlreadyExists.
            FileMode.CreateNew => DokanResult.AlreadyExists,
            // For FileMode.Create or FileMode.Truncate, if the file exists, deny access (read-only).
            FileMode.Create or FileMode.Truncate => DokanResult.AccessDenied,
            // For FileMode.Open, if the file is a directory, deny opening it as a file.
            FileMode.Open when entry.IsDirectory && !info.IsDirectory => DokanResult.PathNotFound,
            // For FileMode.Open, if the file is a file, deny opening it as a directory.
            FileMode.Open when !entry.IsDirectory && info.IsDirectory => DokanResult.NotADirectory,
            _ => DokanResult.Success
        };

        // If we reach here, the operation is likely a read/query on an existing entry, which is allowed.
    }

    private static void LogOperation()
    {
        // Only log debug messages if debug mode is enabled in Program.cs
        // This prevents excessive logging during normal operation
        // Logger.Debug($"{operation}: '{fileName}' (IsDir: {info.IsDirectory}, Context: {info.Context != null})");
    }


    public void Cleanup(string fileName, IDokanFileInfo info)
    {
        // No resources to clean up per file handle in this read-only VFS
        // info.Context = null; // This is already handled by DokanNet internally after Cleanup
    }

    public void CloseFile(string fileName, IDokanFileInfo info)
    {
        // No resources to close per file handle in this read-only VFS
        // info.Context = null; // This is already handled by DokanNet internally after CloseFile
    }

    public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
    {
        bytesRead = 0;

        // The FileEntry for the operation. It can be null if not found.

        // If the entry wasn't in the context, we need to fetch it.
        if (info.Context is not FileEntry entry)
        {
            entry = _vfs.GetEntry(fileName) ?? throw new InvalidOperationException("Entry not found in context during ReadFile");

            // After fetching, cache it in the context for subsequent operations if it's a valid file entry.
            // We only cache valid file entries, not directories, for ReadFile context.
            if (!entry.IsDirectory)
            {
                info.Context = entry;
            }
        }

        // Now, validate the entry we have (either from context or fetched).

        if (entry.IsDirectory)
        {
            // We found something, but it's a directory, not a file.
            Logger.Error($"ReadFile called for a directory: {fileName}");
            return DokanResult.InvalidHandle;
        }

        // At this point, the compiler knows 'entry' is a non-null FileEntry for a file.

        if (offset >= entry.FileSize)
        {
            return DokanResult.Success; // Reading past the end of the file
        }

        try
        {
            // Calculate how much to read, ensuring we don't read past the end of the file
            var remainingBytes = entry.FileSize - offset;
            var bytesToRead = (int)Math.Min(buffer.Length, remainingBytes);

            if (bytesToRead <= 0)
            {
                return DokanResult.Success; // Nothing to read
            }

            // Use the Span overload of ReadFile
            bytesRead = _vfs.ReadFile(entry, buffer.AsSpan(0, bytesToRead), offset);

            return DokanResult.Success;
        }
        catch (Exception ex)
        {
            Logger.Error($"ReadFile error for {fileName}: {ex.Message}\n" +
                         $"An error occurred during ReadFile for '{fileName}' at offset {offset}.");
            _ = ErrorLogger.LogErrorAsync(ex, $"An error occurred during ReadFile for '{fileName}' at offset {offset}.");
            return DokanResult.Error;
        }
    }


    public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
    {
        LogOperation();

        var normalizedPath = fileName.Replace('/', '\\');

        // Handle special paths "." and ".."
        if (normalizedPath is @"\." or @"\..")
        {
            normalizedPath = @"\"; // Treat "\." and "\.." at root as root
        }
        else if (normalizedPath.EndsWith(@"\.", StringComparison.Ordinal) || normalizedPath.EndsWith(@"\..", StringComparison.Ordinal))
        {
            var parentOfSpecial = Path.GetDirectoryName(normalizedPath);
            if (parentOfSpecial == null)
            {
                fileInfo = default;
                return DokanResult.PathNotFound;
            }

            if (normalizedPath.EndsWith(@"\.", StringComparison.Ordinal))
            {
                normalizedPath = parentOfSpecial; // Resolve "\SomeDir\." to "\SomeDir"
            }
            else // EndsWith("\..")
            {
                // Resolve "\SomeDir\.." to the parent of "\SomeDir"
                normalizedPath = Path.GetDirectoryName(parentOfSpecial) ?? @"\"; // Parent of "\SomeDir" is "\"
            }
        }


        // Special case for root directory (after normalization)
        if (normalizedPath == @"\")
        {
            // Get the root entry from the cache (should be there)
            var rootEntry = _vfs.GetEntry(@"\");
            if (rootEntry is not { IsDirectory: true })
            {
                fileInfo = default;
                Logger.Error("Root entry not found or not a directory during GetFileInformation for root.");
                return DokanResult.Error; // Or PathNotFound
            }

            fileInfo = new FileInformation
            {
                FileName = @"\", // Dokan expects "\" for the root
                Attributes = FileAttributes.Directory | FileAttributes.ReadOnly,
                CreationTime = _vfs.VolumeCreationTime,
                LastAccessTime = _vfs.VolumeCreationTime,
                LastWriteTime = _vfs.VolumeCreationTime,
                Length = 0 // Directories have length 0
            };
            return DokanResult.Success;
        }

        // Try to get the entry from context first, then by path
        var entry = info.Context as FileEntry ?? _vfs.GetEntry(normalizedPath);

        if (entry is null)
        {
            fileInfo = default;
            return DokanResult.FileNotFound;
        }

        fileInfo = new FileInformation
        {
            FileName = entry.FileName, // Use the actual file name from the entry
            Attributes = entry.GetWindowsAttributes(),
            CreationTime = _vfs.VolumeCreationTime,
            LastAccessTime = _vfs.VolumeCreationTime,
            LastWriteTime = _vfs.VolumeCreationTime,
            Length = entry.FileSize
        };

        // If the entry was found via path lookup, update the context for future calls
        if (info.Context == null)
        {
            info.Context = entry;
        }

        return DokanResult.Success;
    }

    public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
    {
        LogOperation();

        files = new List<FileInformation>();
        var normalizedPath = fileName.Replace('/', '\\');

        // Get the directory entry for the path being listed
        var dirEntry = _vfs.GetEntry(normalizedPath);
        if (dirEntry is not { IsDirectory: true })
        {
            // If the path isn't a directory or doesn't exist, return appropriate error
            // Note: Dokan usually calls CreateFile first, so this might indicate a logic error
            // or a race condition if the entry was valid in CreateFile but not here.
            // For robustness, check if it's a file not a directory.
            if (dirEntry is { IsDirectory: false })
            {
                Logger.Error($"FindFiles called on a file: {fileName}");
                return DokanResult.NotADirectory;
            }

            Logger.Error($"FindFiles called on non-existent or invalid path: {fileName}");
            return DokanResult.PathNotFound;
        }


        try
        {
            // --- ADD . AND .. ENTRIES ---

            // Add "." entry (current directory)
            files.Add(new FileInformation
            {
                FileName = ".",
                Attributes = FileAttributes.Directory | FileAttributes.ReadOnly,
                CreationTime = _vfs.VolumeCreationTime,
                LastAccessTime = _vfs.VolumeCreationTime,
                LastWriteTime = _vfs.VolumeCreationTime,
                Length = 0
            });

            // Add ".." entry (parent directory), unless it's the root
            if (normalizedPath != @"\")
            {
                files.Add(new FileInformation
                {
                    FileName = "..",
                    Attributes = FileAttributes.Directory | FileAttributes.ReadOnly,
                    CreationTime = _vfs.VolumeCreationTime, // Use volume time or parent dir time if available
                    LastAccessTime = _vfs.VolumeCreationTime,
                    LastWriteTime = _vfs.VolumeCreationTime,
                    Length = 0
                });
            }

            // --- END ADD . AND .. ENTRIES ---


            // Now add the actual entries from the ISO
            var entries = _vfs.GetFolderList(normalizedPath).ToList();

            foreach (var entry in entries)
            {
                // Skip the root entry if it somehow appears in the list (shouldn't happen with GetFolderList logic)
                if (entry.FileName == @"\") continue;

                files.Add(new FileInformation
                {
                    FileName = entry.FileName,
                    Attributes = entry.GetWindowsAttributes(),
                    CreationTime = _vfs.VolumeCreationTime, // XDVDFS doesn't store individual file times, use volume time
                    LastAccessTime = _vfs.VolumeCreationTime,
                    LastWriteTime = _vfs.VolumeCreationTime,
                    Length = entry.FileSize
                });
                // DebugLogger.WriteLine($"[DEBUG] Listing: '{entry.FileName}' Attrs={entry.Attributes}"); // Keep this for debugging if needed
            }

            Logger.Debug($"FindFiles returned {files.Count} entries for '{fileName}' (including . and ..)");
            return DokanResult.Success;
        }
        catch (Exception ex)
        {
            Logger.Error($"FindFiles failed for '{fileName}': {ex.Message}");
            // Log the detailed error asynchronously
            _ = ErrorLogger.LogErrorAsync(ex, $"An error occurred during FindFiles for '{fileName}'.");
            return DokanResult.Error;
        }
    }


    public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features,
        out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
    {
        volumeLabel = "XBOX_ISO";
        fileSystemName = "XDVDFS";
        maximumComponentLength = 255; // Max filename length in XDVDFS is 255
        features = FileSystemFeatures.ReadOnlyVolume |
                   FileSystemFeatures.CasePreservedNames | // XDVDFS is case-insensitive but preserves case
                   FileSystemFeatures.UnicodeOnDisk; // XDVDFS uses ASCII, but Dokan expects Unicode support
        return DokanResult.Success;
    }

    public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
    {
        totalNumberOfBytes = (long)_vfs.VolumeSize;
        totalNumberOfFreeBytes = 0; // Read-only volume
        freeBytesAvailable = 0; // Read-only volume
        return DokanResult.Success;
    }

    public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
    {
        Logger.Info($"Volume mounted at {mountPoint}");
        return DokanResult.Success;
    }

    public NtStatus Unmounted(IDokanFileInfo info)
    {
        Logger.Info("Volume unmounted");
        return DokanResult.Success;
    }

    // --- Read-only operations (AccessDenied) ---
    public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
    {
        bytesWritten = 0;
        return DokanResult.AccessDenied;
    }

    public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }

    public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }

    public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }

    public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }

    public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }

    public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }

    public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }

    public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }

    public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
    {
        return DokanResult.AccessDenied;
    }
    // --- End Read-only operations ---


    // --- Not Implemented Operations ---
    public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
    {
        // Locking is often required by applications, returning Success is usually sufficient for read-only
        return DokanResult.Success;
    }

    public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
    {
        // Unlocking is often required by applications, returning Success is usually sufficient for read-only
        return DokanResult.Success;
    }

    public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
    {
        // For simplicity, we can just call FindFiles and filter the results by pattern
        // Or, return NotImplemented if filtering isn't strictly needed for basic browsing
        // Let's implement basic filtering for better compatibility
        LogOperation();
        files = new List<FileInformation>();

        // Get all files for the directory first
        var status = FindFiles(fileName, out var allFiles, info);
        if (status != DokanResult.Success)
        {
            return status;
        }

        // Filter by pattern (case-insensitive for XDVDFS)
        var pattern = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(searchPattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var fileInfo in allFiles)
        {
            // Match the filename against the pattern
            if (pattern.IsMatch(fileInfo.FileName))
            {
                files.Add(fileInfo);
            }
        }

        Logger.Debug($"FindFilesWithPattern returned {files.Count} entries for '{fileName}' with pattern '{searchPattern}'");
        return DokanResult.Success;
        // files = new List<FileInformation>();
        // return DokanResult.NotImplemented; // Alternative: return NotImplemented
    }

    public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
    {
        // XDVDFS does not support alternate data streams
        streams = new List<FileInformation>();
        return DokanResult.NotImplemented;
    }

    public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
    {
        // Basic security descriptor indicating read-only access for everyone
        // This is a simplified implementation. A full implementation would involve
        // creating a proper DiscretionaryAcl with appropriate AccessAllowed entries.
        // Returning NotImplemented is often acceptable for simple VFS.
        security = new FileSecurity(); // Or DirectorySecurity if it's a directory

        // Try to get the entry to determine if it's a directory
        var entry = _vfs.GetEntry(fileName.Replace('/', '\\'));
        if (entry is not null && entry.IsDirectory)
        {
            security = new DirectorySecurity();
        }
        else
        {
            security = new FileSecurity();
        }

        // Example of allowing read access for Everyone (simplified)
        try
        {
            var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
            const FileSystemRights readRights = FileSystemRights.ReadData | FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions;
            const InheritanceFlags inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            const PropagationFlags propagationFlags = PropagationFlags.None; // Or NoPropagateInherit for directories?

            switch (security)
            {
                case DirectorySecurity ds:
                    ds.AddAccessRule(new FileSystemAccessRule(everyone, readRights | FileSystemRights.ListDirectory, inheritanceFlags, propagationFlags, AccessControlType.Allow));
                    break;
                case FileSecurity fs:
                    fs.AddAccessRule(new FileSystemAccessRule(everyone, readRights, AccessControlType.Allow));
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to create basic security descriptor: {ex.Message}");
            // Fallback to NotImplemented or return an error
            security = new FileSecurity(); // Reset to empty
            return DokanResult.NotImplemented;
        }


        return DokanResult.Success; // Indicate success if a security object was created
        // return DokanResult.NotImplemented; // Alternative: return NotImplemented
    }

    // --- End Not Implemented Operations ---
}
