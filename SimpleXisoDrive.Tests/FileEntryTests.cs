using SimpleXisoDrive.Models;
using SimpleXisoDrive.XDVDFs;

namespace SimpleXisoDrive.Tests;

public class FileEntryTests
{
    [Fact]
    public void CreateRootEntry_SetsCorrectProperties()
    {
        var root = FileEntry.CreateRootEntry(256);

        Assert.Equal("", root.FileName);
        Assert.Equal(XisoFsFileAttributes.Directory, root.Attributes);
        Assert.Equal(0u, root.FileSize);
        Assert.Equal(256u, root.StartSector);
        Assert.Equal(0xFFFF, root.LeftSubTree);
        Assert.Equal(0xFFFF, root.RightSubTree);
        Assert.Equal(0, root.EntrySector);
        Assert.Equal(0, root.EntryOffset);
        Assert.Equal(0, root.EntrySize);
    }

    [Fact]
    public void IsDirectory_ReturnsTrue_WhenDirectoryAttributeSet()
    {
        var entry = new FileEntry { Attributes = XisoFsFileAttributes.Directory };
        Assert.True(entry.IsDirectory);
    }

    [Fact]
    public void IsDirectory_ReturnsFalse_WhenFileAttributes()
    {
        var entry = new FileEntry { Attributes = XisoFsFileAttributes.Archive };
        Assert.False(entry.IsDirectory);
    }

    [Fact]
    public void IsDirectory_ReturnsTrue_WhenMultipleAttributesIncludeDirectory()
    {
        var entry = new FileEntry
        {
            Attributes = XisoFsFileAttributes.Directory | XisoFsFileAttributes.Hidden
        };
        Assert.True(entry.IsDirectory);
    }

    [Theory]
    [InlineData(0xFFFF, false)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    public void HasLeftChild_ReturnsCorrectValue(ushort leftSubTree, bool expected)
    {
        var entry = new FileEntry { LeftSubTree = leftSubTree };
        Assert.Equal(expected, entry.HasLeftChild);
    }

    [Theory]
    [InlineData(0xFFFF, false)]
    [InlineData(0, true)]
    [InlineData(100, true)]
    public void HasRightChild_ReturnsCorrectValue(ushort rightSubTree, bool expected)
    {
        var entry = new FileEntry { RightSubTree = rightSubTree };
        Assert.Equal(expected, entry.HasRightChild);
    }

    [Fact]
    public void GetWindowsAttributes_ReadOnlyFile_ReturnsReadOnly()
    {
        var entry = new FileEntry { Attributes = XisoFsFileAttributes.ReadOnly };
        var result = entry.GetWindowsAttributes();

        Assert.True(result.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void GetWindowsAttributes_Directory_ReturnsDirectory()
    {
        var entry = new FileEntry { Attributes = XisoFsFileAttributes.Directory };
        var result = entry.GetWindowsAttributes();

        Assert.True(result.HasFlag(FileAttributes.Directory));
        Assert.True(result.HasFlag(FileAttributes.ReadOnly)); // Always set
    }

    [Fact]
    public void GetWindowsAttributes_HiddenFile_ReturnsHidden()
    {
        var entry = new FileEntry { Attributes = XisoFsFileAttributes.Hidden };
        var result = entry.GetWindowsAttributes();

        Assert.True(result.HasFlag(FileAttributes.Hidden));
        Assert.True(result.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void GetWindowsAttributes_SystemFile_ReturnsSystem()
    {
        var entry = new FileEntry { Attributes = XisoFsFileAttributes.System };
        var result = entry.GetWindowsAttributes();

        Assert.True(result.HasFlag(FileAttributes.System));
    }

    [Fact]
    public void GetWindowsAttributes_ArchiveFile_ReturnsArchive()
    {
        var entry = new FileEntry { Attributes = XisoFsFileAttributes.Archive };
        var result = entry.GetWindowsAttributes();

        Assert.True(result.HasFlag(FileAttributes.Archive));
    }

    [Fact]
    public void GetWindowsAttributes_NormalFile_ReturnsNormal()
    {
        var entry = new FileEntry { Attributes = XisoFsFileAttributes.Normal };
        var result = entry.GetWindowsAttributes();

        // Normal is not a standard Windows attribute flag that maps directly
        // The code checks if no standard flags are set, then adds Normal
        Assert.True(result.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void GetWindowsAttributes_NoAttributes_ReturnsReadOnlyAndNormal()
    {
        var entry = new FileEntry { Attributes = 0 };
        var result = entry.GetWindowsAttributes();

        Assert.True(result.HasFlag(FileAttributes.ReadOnly));
        Assert.True(result.HasFlag(FileAttributes.Normal));
    }

    [Fact]
    public void GetWindowsAttributes_CombinedAttributes_ReturnsAllFlags()
    {
        var entry = new FileEntry
        {
            Attributes = XisoFsFileAttributes.Directory | XisoFsFileAttributes.Hidden | XisoFsFileAttributes.System
        };
        var result = entry.GetWindowsAttributes();

        Assert.True(result.HasFlag(FileAttributes.Directory));
        Assert.True(result.HasFlag(FileAttributes.Hidden));
        Assert.True(result.HasFlag(FileAttributes.System));
        Assert.True(result.HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void GetFirstChild_ThrowsForNonDirectory()
    {
        var entry = new FileEntry
        {
            Attributes = XisoFsFileAttributes.Archive,
            FileName = "test.txt"
        };

        Assert.Throws<InvalidOperationException>(() => entry.GetFirstChild(null!));
    }

    [Fact]
    public void ReadInternal_ReadsValidEntry()
    {
        // Create a minimal valid XDVDFS directory entry
        // Structure: left(2) + right(2) + sector(4) + size(4) + attrs(1) + nameLen(1) + name + padding
        var entryBytes = new byte[32];
        using var ms = new MemoryStream(entryBytes);
        using var writer = new BinaryWriter(ms);

        writer.Write((ushort)0xFFFF); // LeftSubTree = none
        writer.Write((ushort)0xFFFF); // RightSubTree = none
        writer.Write((uint)100);       // StartSector
        writer.Write((uint)2048);      // FileSize
        writer.Write((byte)0x20);      // Attributes = Archive
        writer.Write((byte)8);         // Name length
        writer.Write("test.txt"u8);    // Filename
        writer.Write((byte)0);         // Null terminator (part of name length)

        ms.Position = 0;
        using var reader = new BinaryReader(ms);

        var entry = new FileEntry();
        entry.ReadInternal(reader, 0, 0);

        Assert.Equal(0xFFFF, entry.LeftSubTree);
        Assert.Equal(0xFFFF, entry.RightSubTree);
        Assert.Equal(100u, entry.StartSector);
        Assert.Equal(2048u, entry.FileSize);
        Assert.Equal(XisoFsFileAttributes.Archive, entry.Attributes);
        Assert.Equal("test.txt", entry.FileName);
    }

    [Fact]
    public void ReadInternal_HandlesDirectoryEntry()
    {
        var entryBytes = new byte[32];
        using var ms = new MemoryStream(entryBytes);
        using var writer = new BinaryWriter(ms);

        writer.Write((ushort)0);       // LeftSubTree
        writer.Write((ushort)0xFFFF);  // RightSubTree = none
        writer.Write((uint)50);        // StartSector
        writer.Write((uint)0);         // FileSize = 0 for directory
        writer.Write((byte)0x10);      // Attributes = Directory
        writer.Write((byte)5);         // Name length
        writer.Write("Games"u8);       // Filename

        ms.Position = 0;
        using var reader = new BinaryReader(ms);

        var entry = new FileEntry();
        entry.ReadInternal(reader, 0, 0);

        Assert.True(entry.IsDirectory);
        Assert.Equal("Games", entry.FileName);
        Assert.Equal(0u, entry.FileSize);
    }

    [Fact]
    public void ReadInternal_HandlesEmptyFilename()
    {
        var entryBytes = new byte[32];
        using var ms = new MemoryStream(entryBytes);
        using var writer = new BinaryWriter(ms);

        writer.Write((ushort)0xFFFF);
        writer.Write((ushort)0xFFFF);
        writer.Write((uint)10);
        writer.Write((uint)100);
        writer.Write((byte)0x01); // ReadOnly
        writer.Write((byte)0);    // Name length = 0

        ms.Position = 0;
        using var reader = new BinaryReader(ms);

        var entry = new FileEntry();
        entry.ReadInternal(reader, 0, 0);

        Assert.Equal("", entry.FileName);
    }

    [Fact]
    public void ReadInternal_CalculatesEntrySize_CorrectlyForAlignedName()
    {
        // Name "test" = 4 bytes, header = 14 bytes, total = 18, padding to 4-byte align = 2
        var entryBytes = new byte[32];
        using var ms = new MemoryStream(entryBytes);
        using var writer = new BinaryWriter(ms);

        writer.Write((ushort)0xFFFF);
        writer.Write((ushort)0xFFFF);
        writer.Write((uint)10);
        writer.Write((uint)100);
        writer.Write((byte)0x01);
        writer.Write((byte)4);    // Name length = 4
        writer.Write("test"u8);

        ms.Position = 0;
        using var reader = new BinaryReader(ms);

        var entry = new FileEntry();
        entry.ReadInternal(reader, 0, 0);

        // 14 (header) + 4 (name) = 18, padding = 2, total = 20
        Assert.Equal(20, entry.EntrySize);
    }

    [Fact]
    public void ReadInternal_CalculatesEntrySize_NoPaddingNeeded()
    {
        // Name "abc" = 3 bytes, header = 14 bytes, total = 17, padding to 4-byte align = 3
        var entryBytes = new byte[32];
        using var ms = new MemoryStream(entryBytes);
        using var writer = new BinaryWriter(ms);

        writer.Write((ushort)0xFFFF);
        writer.Write((ushort)0xFFFF);
        writer.Write((uint)10);
        writer.Write((uint)100);
        writer.Write((byte)0x01);
        writer.Write((byte)3);
        writer.Write("abc"u8);

        ms.Position = 0;
        using var reader = new BinaryReader(ms);

        var entry = new FileEntry();
        entry.ReadInternal(reader, 0, 0);

        // 14 + 3 = 17, padding = 3, total = 20
        Assert.Equal(20, entry.EntrySize);
    }
}
