using SimpleXisoDrive.Services;

namespace SimpleXisoDrive.XDVDFs;

/// <inheritdoc />
/// <summary>
/// Manages thread-safe access to the ISO file stream.
/// </summary>
public class IsoSt : IDisposable
{
    public const int SectorSize = 2048; // XDVDFS sector size is always 2048 bytes
    private readonly FileStream _fileStream;

    // Global offset for the volume (e.g. for dual-layer/hybrid discs)
    public long VolumeOffset { get; set; }

    // Expose the lock object for operations that need to perform multiple reads under one lock
    public object LockObject { get; } = new();

    // Keep Reader private or internal, access should go through locked methods
    internal BinaryReader Reader { get; }

    public IsoSt(string isoPath)
    {
        // Use FileShare.ReadWrite to allow other processes (like antivirus) to open the file
        // while SimpleXisoDrive has it open.
        _fileStream = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Reader = new BinaryReader(_fileStream);
    }

    /// <summary>
    /// Reads file data for a given entry and offset, thread-safely.
    /// </summary>
    public int Read(FileEntry entry, Span<byte> buffer, long offset)
    {
        lock (LockObject)
        {
            try
            {
                // Apply VolumeOffset to the calculation
                var fileOffset = VolumeOffset + (long)entry.StartSector * FileEntry.SectorSize + offset;

                // Ensure we don't seek past the end of the stream
                if (fileOffset >= _fileStream.Length)
                {
                    return 0;
                }

                _fileStream.Seek(fileOffset, SeekOrigin.Begin);

                var totalRead = 0;
                while (totalRead < buffer.Length)
                {
                    var n = _fileStream.Read(buffer[totalRead..]);
                    if (n == 0) break; // End of stream reached

                    totalRead += n;
                }

                return totalRead;
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"Read error at sector {entry.StartSector}, offset {offset}: {ex.Message}");
                _ = ErrorLogger.LogErrorAsync(ex, $"Physical Read Failure: Sector {entry.StartSector}, Offset {offset}, File: {entry.FileName}");
                return 0;
            }
        }
    }

    /// <summary>
    /// Reads a FileEntry structure from a specific sector and offset, thread-safely.
    /// </summary>
    public FileEntry? ReadFileEntry(long sector, long offset)
    {
        lock (LockObject)
        {
            try
            {
                // Apply VolumeOffset to the calculation
                var position = VolumeOffset + sector * SectorSize + offset;

                if (position >= _fileStream.Length)
                {
                    throw new EndOfStreamException($"Position {position} is beyond file length");
                }

                _fileStream.Seek(position, SeekOrigin.Begin);
                var entry = new FileEntry();
                entry.ReadInternal(Reader, sector, offset); // This reads one entry

                // Now, for traversal, we need to return the entry but ensure the next read knows the size
                return entry;
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"Failed to read FileEntry at sector {sector}, offset {offset}: {ex.Message}");
                _ = ErrorLogger.LogErrorAsync(ex, $"Failed to read FileEntry at sector {sector}, offset {offset}");
                return null;
            }
        }
    }

    /// <summary>
    /// Executes an action with the BinaryReader under the stream lock.
    /// Useful for reading structures that require multiple sequential reads after a single seek.
    /// </summary>
    public void ExecuteLocked(Action<BinaryReader> action)
    {
        lock (LockObject)
        {
            action(Reader);
        }
    }

    public void Dispose()
    {
        Reader.Dispose();
        GC.SuppressFinalize(this);
    }
}