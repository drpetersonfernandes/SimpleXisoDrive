using System.Text;
using SimpleXisoDrive.Models;
using SimpleXisoDrive.Services;

namespace SimpleXisoDrive.XDVDFs;

public class FileEntry
{
    public long EntrySector { get; internal set; }
    public const int SectorSize = 2048;
    public ushort LeftSubTree { get; internal set; }
    public ushort RightSubTree { get; internal set; }
    public uint StartSector { get; internal set; }
    public uint FileSize { get; internal set; }
    public XisoFsFileAttributes Attributes { get; internal set; }
    public string FileName { get; internal set; }
    public long EntryOffset { get; set; }
    public int EntrySize { get; internal set; } // Total size of this entry in bytes
    public bool IsDirectory => (Attributes & XisoFsFileAttributes.Directory) != 0;
    public bool HasLeftChild => LeftSubTree != 0xFFFF;
    public bool HasRightChild => RightSubTree != 0xFFFF;

    internal FileEntry()
    {
        FileName = string.Empty;
    }

    public static FileEntry CreateRootEntry(uint rootDirTableSector)
    {
        return new FileEntry
        {
            FileName = "",
            Attributes = XisoFsFileAttributes.Directory,
            FileSize = 0,
            StartSector = rootDirTableSector,
            LeftSubTree = 0xFFFF,
            RightSubTree = 0xFFFF,
            EntrySector = 0,
            EntryOffset = 0,
            EntrySize = 0
        };
    }

    internal void ReadInternal(BinaryReader reader, long sector, long offset)
    {
        try
        {
            EntrySector = sector;
            EntryOffset = offset;

            // Read fixed-size fields (14 bytes total)
            LeftSubTree = reader.ReadUInt16(); // 2 bytes
            RightSubTree = reader.ReadUInt16(); // 2 bytes
            StartSector = reader.ReadUInt32(); // 4 bytes
            FileSize = reader.ReadUInt32(); // 4 bytes
            Attributes = (XisoFsFileAttributes)reader.ReadByte(); // 1 byte
            var nameLength = reader.ReadByte(); // 1 byte

            // Read the filename
            byte[] nameBytes;
            if (nameLength > 0)
            {
                nameBytes = reader.ReadBytes(nameLength);
                if (nameBytes.Length < nameLength)
                {
                    throw new EndOfStreamException("Could not read complete filename");
                }
            }
            else
            {
                nameBytes = [];
            }

            // Process filename - XDVDFS uses null-terminated ASCII strings
            if (nameBytes.Length > 0)
            {
                var rawString = Encoding.ASCII.GetString(nameBytes);
                var nullIndex = rawString.IndexOf('\0');
                if (nullIndex >= 0)
                {
                    rawString = rawString.Substring(0, nullIndex);
                }

                FileName = rawString.Trim();
            }
            else
            {
                FileName = string.Empty;
            }

            // Calculate actual entry size
            EntrySize = 14 + nameLength; // Fixed header (14 bytes) + variable filename length

            // Add padding to align to the 4-byte boundary (XDVDFS requirement)
            var padding = (4 - EntrySize % 4) % 4;
            EntrySize += padding;

            // Skip the padding bytes
            if (padding > 0)
            {
                if (reader.BaseStream.Position + padding > reader.BaseStream.Length)
                {
                    throw new EndOfStreamException("End of stream reached while skipping padding bytes");
                }

                reader.BaseStream.Seek(padding, SeekOrigin.Current);
            }

            // Validate the entry
            if (FileSize == uint.MaxValue)
            {
                DebugLogger.WriteLine($"Suspicious FileSize detected: {FileSize} for '{FileName}'");
            }

            // DebugLogger.WriteLine($"Read FileEntry: '{FileName}' at sector {sector}, offset {offset} (Size: {FileSize}, EntrySize: {EntrySize}, L:{LeftSubTree}, R:{RightSubTree})");
        }
        catch (Exception ex)
        {
            DebugLogger.WriteLine($"Error reading FileEntry at sector {sector}, offset {offset}: {ex.Message}");
            FileName = "Invalid Entry";
            FileSize = 0;
            EntrySize = 16; // Minimum aligned size
            throw;
        }
    }

    public FileEntry? GetLeftChild(IsoSt isoSt)
    {
        if (LeftSubTree == 0xFFFF) return null;

        // XDVDFS stores offsets as index * 4
        var childOffset = (long)LeftSubTree * 4;

        // Prevent self-reference (compare byte offsets)
        if (childOffset != EntryOffset)
            return isoSt.ReadFileEntry(EntrySector, childOffset);

        // DebugLogger.WriteLine($"Invalid self-reference in LeftSubTree for entry at sector {EntrySector}, offset {EntryOffset}");
        return null;
    }

    public FileEntry? GetRightChild(IsoSt isoSt)
    {
        if (RightSubTree == 0xFFFF) return null;

        // XDVDFS stores offsets as index * 4
        var childOffset = (long)RightSubTree * 4;

        if (childOffset != EntryOffset)
            return isoSt.ReadFileEntry(EntrySector, childOffset);

        // DebugLogger.WriteLine($"Invalid self-reference in RightSubTree for entry at sector {EntrySector}, offset {EntryOffset}");
        return null;
    }

    public FileEntry? GetFirstChild(IsoSt isoSt)
    {
        if (!IsDirectory)
            throw new InvalidOperationException("Not a directory");

        // For directories, StartSector points to the directory table sector
        // The first entry is always at offset 0 of that sector
        return isoSt.ReadFileEntry(StartSector, 0);
    }

    public FileAttributes GetWindowsAttributes()
    {
        var winAttrs = FileAttributes.ReadOnly;
        if ((Attributes & XisoFsFileAttributes.Directory) != 0)
        {
            winAttrs |= FileAttributes.Directory;
        }

        if ((Attributes & XisoFsFileAttributes.Hidden) != 0)
        {
            winAttrs |= FileAttributes.Hidden;
        }

        if ((Attributes & XisoFsFileAttributes.System) != 0)
        {
            winAttrs |= FileAttributes.System;
        }

        if ((Attributes & XisoFsFileAttributes.Archive) != 0)
        {
            winAttrs |= FileAttributes.Archive;
        }

        const FileAttributes standardWindowsAttributes = FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;
        if ((winAttrs & standardWindowsAttributes) == 0)
        {
            winAttrs |= FileAttributes.Normal;
        }

        return winAttrs;
    }
}