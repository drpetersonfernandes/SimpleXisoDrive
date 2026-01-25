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

    /// <summary>
    /// Helper to ensure every single operation is tracked.
    /// If any logic fails, the bug is sent to the API endpoint.
    /// </summary>
    private static NtStatus ExecuteWithReporting(string operation, string fileName, Func<NtStatus> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            Logger.Error($"[BUG REPORTED] {operation} failed for '{fileName}': {ex.Message}");

            // Fire and forget the error report so the filesystem remains responsive
            _ = ErrorLogger.LogErrorAsync(ex, $"Dokan Operation: {operation} | File: {fileName}");

            return DokanResult.Error;
        }
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('/', '\\');
        if (normalized is @"\." or @"\..") return @"\";

        if (normalized.EndsWith(@"\.", StringComparison.Ordinal))
            return Path.GetDirectoryName(normalized) ?? @"\";

        if (normalized.EndsWith(@"\..", StringComparison.Ordinal))
        {
            var parent = Path.GetDirectoryName(normalized);
            return parent == null ? @"\" : Path.GetDirectoryName(parent) ?? @"\";
        }

        return normalized;
    }

    public NtStatus CreateFile(string fileName, FileAccess access, FileShare share,
        FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
    {
        return ExecuteWithReporting(nameof(CreateFile), fileName, () =>
        {
            var path = NormalizePath(fileName);

            if (path == @"\")
            {
                var rootEntry = _vfs.GetEntry(@"\");
                if (rootEntry is not { IsDirectory: true }) return DokanResult.Error;

                info.IsDirectory = true;
                info.Context = rootEntry;
                return DokanResult.Success;
            }

            var entry = _vfs.GetEntry(path);
            if (entry == null)
            {
                return mode == FileMode.Open ? DokanResult.FileNotFound : DokanResult.AccessDenied;
            }

            info.IsDirectory = entry.IsDirectory;
            info.Context = entry;

            // Deny write access (Read-Only FS)
            if ((access & (FileAccess.GenericWrite | FileAccess.WriteData | FileAccess.AppendData | FileAccess.Delete)) != 0)
            {
                return DokanResult.AccessDenied;
            }

            return mode switch
            {
                FileMode.CreateNew => DokanResult.AlreadyExists,
                FileMode.Create or FileMode.Truncate => DokanResult.AccessDenied,
                FileMode.Open when entry.IsDirectory && !info.IsDirectory => DokanResult.PathNotFound,
                FileMode.Open when !entry.IsDirectory && info.IsDirectory => DokanResult.NotADirectory,
                _ => DokanResult.Success
            };
        });
    }

    public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
    {
        var internalBytesRead = 0;
        var result = ExecuteWithReporting(nameof(ReadFile), fileName, () =>
        {
            if (info.Context is not FileEntry entry)
            {
                entry = _vfs.GetEntry(fileName) ?? throw new InvalidOperationException("FileEntry missing");
                info.Context = entry;
            }

            if (entry.IsDirectory) return DokanResult.InvalidHandle;
            if (offset >= entry.FileSize) return DokanResult.Success;

            var remainingBytes = entry.FileSize - offset;
            var bytesToRead = (int)Math.Min(buffer.Length, remainingBytes);

            if (bytesToRead > 0)
            {
                internalBytesRead = _vfs.ReadFile(entry, buffer.AsSpan(0, bytesToRead), offset);
            }

            return DokanResult.Success;
        });

        bytesRead = internalBytesRead;
        return result;
    }

    public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
    {
        FileInformation internalInfo = default;
        var result = ExecuteWithReporting(nameof(GetFileInformation), fileName, () =>
        {
            var path = NormalizePath(fileName);
            var entry = info.Context as FileEntry ?? _vfs.GetEntry(path);

            if (entry == null) return DokanResult.FileNotFound;

            internalInfo = new FileInformation
            {
                FileName = entry.FileName,
                Attributes = entry.GetWindowsAttributes(),
                CreationTime = _vfs.VolumeCreationTime,
                LastAccessTime = _vfs.VolumeCreationTime,
                LastWriteTime = _vfs.VolumeCreationTime,
                Length = entry.IsDirectory ? 0 : entry.FileSize
            };

            return DokanResult.Success;
        });

        fileInfo = internalInfo;
        return result;
    }

    public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
    {
        var internalFiles = new List<FileInformation>();
        var result = ExecuteWithReporting(nameof(FindFiles), fileName, () =>
        {
            var path = NormalizePath(fileName);
            var dirEntry = _vfs.GetEntry(path);

            if (dirEntry is not { IsDirectory: true }) return DokanResult.NotADirectory;

            // Add virtual entries
            var template = new FileInformation
            {
                Attributes = FileAttributes.Directory | FileAttributes.ReadOnly,
                CreationTime = _vfs.VolumeCreationTime, LastAccessTime = _vfs.VolumeCreationTime, LastWriteTime = _vfs.VolumeCreationTime
            };

            internalFiles.Add(new FileInformation { FileName = ".", Attributes = template.Attributes, CreationTime = template.CreationTime });
            if (path != @"\") internalFiles.Add(new FileInformation { FileName = "..", Attributes = template.Attributes, CreationTime = template.CreationTime });

            foreach (var entry in _vfs.GetFolderList(path))
            {
                if (entry.FileName == @"\") continue;

                internalFiles.Add(new FileInformation
                {
                    FileName = entry.FileName,
                    Attributes = entry.GetWindowsAttributes(),
                    CreationTime = _vfs.VolumeCreationTime,
                    LastAccessTime = _vfs.VolumeCreationTime,
                    LastWriteTime = _vfs.VolumeCreationTime,
                    Length = entry.FileSize
                });
            }

            return DokanResult.Success;
        });

        files = internalFiles;
        return result;
    }

    public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
    {
        var filteredFiles = new List<FileInformation>();
        var result = ExecuteWithReporting(nameof(FindFilesWithPattern), fileName, () =>
        {
            var status = FindFiles(fileName, out var allFiles, info);
            if (status != DokanResult.Success) return status;

            var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(searchPattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            var regex = new System.Text.RegularExpressions.Regex(regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            foreach (var f in allFiles)
            {
                if (f.FileName is "." or ".." || regex.IsMatch(f.FileName))
                    filteredFiles.Add(f);
            }

            return DokanResult.Success;
        });

        files = filteredFiles;
        return result;
    }

    public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity? security, AccessControlSections sections, IDokanFileInfo info)
    {
        FileSystemSecurity? internalSecurity = null;
        var result = ExecuteWithReporting(nameof(GetFileSecurity), fileName, () =>
        {
            var entry = _vfs.GetEntry(NormalizePath(fileName));
            internalSecurity = entry is { IsDirectory: true } ? new DirectorySecurity() : new FileSecurity();

            var everyone = new System.Security.Principal.SecurityIdentifier(System.Security.Principal.WellKnownSidType.WorldSid, null);
            internalSecurity.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.ReadAndExecute, AccessControlType.Allow));

            return DokanResult.Success;
        });

        security = internalSecurity;
        return result;
    }

    public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
    {
        volumeLabel = "XBOX_ISO";
        fileSystemName = "XDVDFS";
        maximumComponentLength = 255;
        features = FileSystemFeatures.ReadOnlyVolume | FileSystemFeatures.CasePreservedNames | FileSystemFeatures.UnicodeOnDisk;

        try
        {
            return DokanResult.Success;
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogErrorAsync(ex, "GetVolumeInformation failed");
            return DokanResult.Error;
        }
    }

    public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
    {
        try
        {
            totalNumberOfBytes = (long)_vfs.VolumeSize;
            freeBytesAvailable = 0;
            totalNumberOfFreeBytes = 0;
            return DokanResult.Success;
        }
        catch (Exception ex)
        {
            freeBytesAvailable = 0;
            totalNumberOfBytes = 0;
            totalNumberOfFreeBytes = 0;
            _ = ErrorLogger.LogErrorAsync(ex, "GetDiskFreeSpace failed");
            return DokanResult.Error;
        }
    }

    // Boilerplate / Read-Only Enforcement
    public void Cleanup(string fileName, IDokanFileInfo info)
    {
    }

    public void CloseFile(string fileName, IDokanFileInfo info)
    {
    }

    public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
    {
        return DokanResult.Success;
    }

    public NtStatus Unmounted(IDokanFileInfo info)
    {
        return DokanResult.Success;
    }

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

    public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
    {
        return DokanResult.Success;
    }

    public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
    {
        return DokanResult.Success;
    }

    public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
    {
        streams = new List<FileInformation>();
        return DokanResult.NotImplemented;
    }
}