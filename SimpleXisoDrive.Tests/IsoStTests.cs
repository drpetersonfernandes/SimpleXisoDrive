using SimpleXisoDrive.XDVDFs;

namespace SimpleXisoDrive.Tests;

public class IsoStTests
{
    [Fact]
    public void Constructor_WithStream_SetsVolumeOffsetToZero()
    {
        using var ms = new MemoryStream(new byte[1024]);
        using var isoSt = new IsoSt(ms);

        Assert.Equal(0, isoSt.VolumeOffset);
    }

    [Fact]
    public void VolumeOffset_CanBeSet()
    {
        using var ms = new MemoryStream(new byte[1024]);
        using var isoSt = new IsoSt(ms);

        isoSt.VolumeOffset = 4096;
        Assert.Equal(4096, isoSt.VolumeOffset);
    }

    [Fact]
    public void SectorSize_Is2048()
    {
        Assert.Equal(2048, IsoSt.SectorSize);
    }

    [Fact]
    public void ExecuteLocked_ExecutesAction()
    {
        using var ms = new MemoryStream(new byte[1024]);
        using var isoSt = new IsoSt(ms);

        var executed = false;
        isoSt.ExecuteLocked(reader =>
        {
            executed = true;
            Assert.NotNull(reader);
        });

        Assert.True(executed);
    }

    [Fact]
    public void ExecuteLocked_ProvidesBinaryReader()
    {
        var data = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        using var ms = new MemoryStream(data);
        using var isoSt = new IsoSt(ms);

        isoSt.ExecuteLocked(reader =>
        {
            var value = reader.ReadByte();
            Assert.Equal(0x01, value);
        });
    }

    [Fact]
    public void ExecuteLocked_SeeksCorrectly()
    {
        var data = new byte[] { 0x00, 0x00, 0x00, 0x00, 0xAB, 0xCD };
        using var ms = new MemoryStream(data);
        using var isoSt = new IsoSt(ms);

        isoSt.ExecuteLocked(reader =>
        {
            reader.BaseStream.Seek(4, SeekOrigin.Begin);
            var value = reader.ReadUInt16();
            Assert.Equal(0xCDAB, value); // Little-endian
        });
    }

    [Fact]
    public void Read_ReturnsZero_WhenOffsetBeyondStream()
    {
        var data = new byte[100];
        using var ms = new MemoryStream(data);
        using var isoSt = new IsoSt(ms);

        var entry = new FileEntry { StartSector = 1000 };
        var buffer = new byte[10];

        var read = isoSt.Read(entry, buffer, 0);
        Assert.Equal(0, read);
    }

    [Fact]
    public void Read_ReadsData_FromCorrectOffset()
    {
        // Sector 1, offset 0 = byte 2048
        var data = new byte[4096];
        data[2048] = 0xAA;
        data[2049] = 0xBB;

        using var ms = new MemoryStream(data);
        using var isoSt = new IsoSt(ms);

        var entry = new FileEntry { StartSector = 1 };
        var buffer = new byte[2];

        var read = isoSt.Read(entry, buffer, 0);
        Assert.Equal(2, read);
        Assert.Equal(0xAA, buffer[0]);
        Assert.Equal(0xBB, buffer[1]);
    }

    [Fact]
    public void Read_AppliesVolumeOffset()
    {
        var data = new byte[8192];
        data[4096] = 0xCC;

        using var ms = new MemoryStream(data);
        using var isoSt = new IsoSt(ms);
        isoSt.VolumeOffset = 2048;

        var entry = new FileEntry { StartSector = 1 };
        var buffer = new byte[1];

        var read = isoSt.Read(entry, buffer, 0);
        Assert.Equal(1, read);
        Assert.Equal(0xCC, buffer[0]);
    }

    [Fact]
    public void ReadFileEntry_ReadsValidEntry()
    {
        // Create a minimal file entry at sector 1, offset 0
        var data = new byte[4096];
        using var ms = new MemoryStream(data);
        using var writer = new BinaryWriter(ms);

        // Seek to sector 1 (byte 2048)
        writer.Seek(2048, SeekOrigin.Begin);
        writer.Write((ushort)0xFFFF); // LeftSubTree
        writer.Write((ushort)0xFFFF); // RightSubTree
        writer.Write((uint)10);       // StartSector
        writer.Write((uint)100);      // FileSize
        writer.Write((byte)0x20);     // Attributes = Archive
        writer.Write((byte)4);        // Name length
        writer.Write("test"u8);       // Filename

        ms.Position = 0;
        using var isoSt = new IsoSt(ms);

        var entry = isoSt.ReadFileEntry(1, 0);
        Assert.NotNull(entry);
        Assert.Equal("test", entry.FileName);
        Assert.Equal(10u, entry.StartSector);
        Assert.Equal(100u, entry.FileSize);
    }

    [Fact]
    public void ReadFileEntry_ReturnsNull_WhenBeyondStream()
    {
        using var ms = new MemoryStream(new byte[100]);
        using var isoSt = new IsoSt(ms);

        var entry = isoSt.ReadFileEntry(1000, 0);
        Assert.Null(entry);
    }

    [Fact]
    public void Dispose_DisposesReader()
    {
        var ms = new MemoryStream(new byte[100]);
        var isoSt = new IsoSt(ms);

        isoSt.Dispose();

        // After dispose, the stream should be closed
        Assert.Throws<ObjectDisposedException>(() => ms.ReadByte());
    }
}
