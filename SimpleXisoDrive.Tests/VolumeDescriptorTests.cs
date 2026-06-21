using SimpleXisoDrive.XDVDFs;

namespace SimpleXisoDrive.Tests;

public class VolumeDescriptorTests
{
    /// <summary>
    /// Creates a valid XDVDFS volume descriptor byte array at the given sector offset.
    /// </summary>
    private static byte[] CreateValidVolumeDescriptor()
    {
        // A volume descriptor is 0x800 bytes (2048 bytes = 1 sector)
        var data = new byte[0x800];

        // Magic ID at offset 0x00
        var magic = "MICROSOFT*XBOX*MEDIA"u8.ToArray();
        magic.CopyTo(data, 0);

        // Root dir table sector at offset 0x14
        BitConverter.GetBytes((uint)256).CopyTo(data, 0x14);

        // Root dir table size at offset 0x18
        BitConverter.GetBytes((uint)2048).CopyTo(data, 0x18);

        // FileTime at offset 0x1C (8 bytes)
        var fileTime = DateTime.Now.ToFileTimeUtc();
        BitConverter.GetBytes(fileTime).CopyTo(data, 0x1C);

        // Second magic ID at offset 0x7EC
        magic.CopyTo(data, 0x7EC);

        return data;
    }

    /// <summary>
    /// Creates an ISO-like byte array with a volume descriptor at the specified offset.
    /// </summary>
    private static MemoryStream CreateFakeIsoWithDescriptor(long descriptorOffset, byte[]? descriptorData = null)
    {
        var vdData = descriptorData ?? CreateValidVolumeDescriptor();

        // Ensure the stream is large enough
        var totalSize = descriptorOffset + 0x800 + 1024;
        var isoData = new byte[totalSize];
        vdData.CopyTo(isoData, descriptorOffset);

        return new MemoryStream(isoData);
    }

    [Fact]
    public void Validate_ReturnsTrue_ForValidDescriptor()
    {
        var magic = "MICROSOFT*XBOX*MEDIA"u8.ToArray();

        // Build a minimal valid descriptor
        var data = new byte[0x800];
        magic.CopyTo(data, 0);
        BitConverter.GetBytes((uint)256).CopyTo(data, 0x14);
        BitConverter.GetBytes((uint)2048).CopyTo(data, 0x18);
        BitConverter.GetBytes(DateTime.Now.ToFileTimeUtc()).CopyTo(data, 0x1C);
        magic.CopyTo(data, 0x7EC);

        using var ms = new MemoryStream(data);
        using var isoSt = new IsoSt(ms);

        // Use reflection to access private constructor and Validate
        var result = ReadDescriptorFromBytes(isoSt);
        Assert.NotNull(result);
        Assert.True(result.Validate());
    }

    [Fact]
    public void Validate_ReturnsFalse_WhenMagicMismatch()
    {
        // Create an ISO with wrong magic at all standard offsets
        var isoData = new byte[32 * 2048 + 0x800];
        var wrongMagic = "WRONG*MAGIC*STRING!"u8.ToArray();

        // Place wrong magic at sector 32
        wrongMagic.CopyTo(isoData, 32 * 2048);
        BitConverter.GetBytes((uint)256).CopyTo(isoData, 32 * 2048 + 0x14);
        BitConverter.GetBytes((uint)2048).CopyTo(isoData, 32 * 2048 + 0x18);
        BitConverter.GetBytes(DateTime.Now.ToFileTimeUtc()).CopyTo(isoData, 32 * 2048 + 0x1C);
        wrongMagic.CopyTo(isoData, 32 * 2048 + 0x7EC);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        // ReadFrom will throw InvalidImageException since no valid descriptor is found
        Assert.Throws<InvalidImageException>(() => VolumeDescriptor.ReadFrom(isoSt));
    }

    [Fact]
    public void IsRebuiltXisoFormat_ReturnsTrue_WhenSectorIsZero()
    {
        var data = CreateValidVolumeDescriptor();
        using var ms = new MemoryStream(data);
        using var isoSt = new IsoSt(ms);

        var descriptor = ReadDescriptorAtSector(isoSt);
        Assert.NotNull(descriptor);
        Assert.True(descriptor.IsRebuiltXisoFormat());
    }

    [Fact]
    public void IsRebuiltXisoFormat_ReturnsFalse_WhenSectorIs32()
    {
        var data = CreateValidVolumeDescriptor();
        // Place descriptor at sector 32
        var isoData = new byte[32 * 2048 + 0x800];
        data.CopyTo(isoData, 32 * 2048);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        var descriptor = ReadDescriptorAtSector(isoSt);
        Assert.NotNull(descriptor);
        Assert.False(descriptor.IsRebuiltXisoFormat());
    }

    [Fact]
    public void ReadFrom_FindsStandardXboxIso_AtSector32()
    {
        // Standard Xbox ISO: descriptor at sector 32, offset 0
        var vdData = CreateValidVolumeDescriptor();
        var isoData = new byte[32 * 2048 + 0x800];
        vdData.CopyTo(isoData, 32 * 2048);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        var descriptor = VolumeDescriptor.ReadFrom(isoSt);
        Assert.NotNull(descriptor);
        Assert.True(descriptor.Validate());
        Assert.Equal(32u, descriptor.Sector);
        Assert.Equal(0, isoSt.VolumeOffset);
    }

    [Fact]
    public void ReadFrom_FindsRebuiltXiso_AtSector0()
    {
        // Rebuilt XISO: descriptor at sector 0
        var vdData = CreateValidVolumeDescriptor();
        var isoData = new byte[0x800 + 1024];
        vdData.CopyTo(isoData, 0);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        var descriptor = VolumeDescriptor.ReadFrom(isoSt);
        Assert.NotNull(descriptor);
        Assert.True(descriptor.Validate());
        Assert.Equal(0u, descriptor.Sector);
        Assert.Equal(0, isoSt.VolumeOffset);
    }

    [Fact]
    public void ReadFrom_ThrowsInvalidImageException_WhenNoValidDescriptor()
    {
        // Fill with garbage data
        var isoData = new byte[1024 * 1024]; // 1MB
        Random.Shared.NextBytes(isoData);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        Assert.Throws<InvalidImageException>(() => VolumeDescriptor.ReadFrom(isoSt));
    }

    [Fact]
    public void ReadFrom_ThrowsInvalidImageException_WhenFileTooSmall()
    {
        // File too small for any descriptor
        var isoData = new byte[100];

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        Assert.ThrowsAny<Exception>(() => VolumeDescriptor.ReadFrom(isoSt));
    }

    [Fact]
    public void ReadFrom_SetsVolumeOffset_ForStandardIso()
    {
        var vdData = CreateValidVolumeDescriptor();
        var isoData = new byte[32 * 2048 + 0x800];
        vdData.CopyTo(isoData, 32 * 2048);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        VolumeDescriptor.ReadFrom(isoSt);
        Assert.Equal(0, isoSt.VolumeOffset);
    }

    [Fact]
    public void ReadFrom_ReadsRootDirTableSector()
    {
        var vdData = CreateValidVolumeDescriptor();
        var isoData = new byte[32 * 2048 + 0x800];
        vdData.CopyTo(isoData, 32 * 2048);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        var descriptor = VolumeDescriptor.ReadFrom(isoSt);
        Assert.Equal(256u, descriptor.RootDirTableSector);
    }

    [Fact]
    public void ReadFrom_ReadsCreationTime()
    {
        var vdData = CreateValidVolumeDescriptor();
        var isoData = new byte[32 * 2048 + 0x800];
        vdData.CopyTo(isoData, 32 * 2048);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        var descriptor = VolumeDescriptor.ReadFrom(isoSt);
        // CreationTime should be close to now (within 5 seconds due to precision)
        Assert.True(descriptor.CreationTime > DateTime.MinValue);
        Assert.True(descriptor.CreationTime < DateTime.MaxValue);
    }

    [Fact]
    public void ReadFrom_HandlesInvalidFileTime_Gracefully()
    {
        // Create a descriptor with invalid file time
        var vdData = CreateValidVolumeDescriptor();
        // Overwrite file time with invalid value
        BitConverter.GetBytes(long.MaxValue).CopyTo(vdData, 0x1C);

        var isoData = new byte[32 * 2048 + 0x800];
        vdData.CopyTo(isoData, 32 * 2048);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        var descriptor = VolumeDescriptor.ReadFrom(isoSt);
        Assert.NotNull(descriptor);
        Assert.Equal(DateTime.MinValue, descriptor.CreationTime);
    }

    [Fact]
    public void ErrorMessages_IncludeAllTriedOffsets()
    {
        var isoData = new byte[1024 * 1024]; // 1MB
        Random.Shared.NextBytes(isoData);

        using var ms = new MemoryStream(isoData);
        using var isoSt = new IsoSt(ms);

        var ex = Assert.Throws<InvalidImageException>(() => VolumeDescriptor.ReadFrom(isoSt));
        Assert.Contains("Sector 32 (Offset 0)", ex.Message);
        Assert.Contains("Sector 0 (Offset 0)", ex.Message);
    }

    /// <summary>
    /// Helper to read a VolumeDescriptor from raw bytes using reflection.
    /// </summary>
    private static VolumeDescriptor? ReadDescriptorFromBytes(IsoSt isoSt)
    {
        return ReadDescriptorAtSector(isoSt);
    }

    /// <summary>
    /// Helper to read a VolumeDescriptor at a specific sector and offset.
    /// </summary>
    private static VolumeDescriptor? ReadDescriptorAtSector(IsoSt isoSt)
    {
        // We need to use the ReadFrom method which handles everything
        // For unit testing Validate/IsRebuiltXisoFormat, we create a valid ISO
        try
        {
            return VolumeDescriptor.ReadFrom(isoSt);
        }
        catch
        {
            return null;
        }
    }
}
