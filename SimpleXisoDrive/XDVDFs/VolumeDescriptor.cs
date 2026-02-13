using SimpleXisoDrive.Services;

namespace SimpleXisoDrive.XDVDFs;

public class VolumeDescriptor
{
    public uint Sector { get; }
    private const int VolumeDescriptorSector = 32;

    // Offset used in xbox-iso-vfs for dual layer / game partitions
    // 2048 * 32 * 6192 = 405,798,912 bytes
    private const long GamePartitionOffset = 2048L * 32 * 6192;

    private static readonly byte[] MagicId = "MICROSOFT*XBOX*MEDIA"u8.ToArray();

    private byte[] Id1 { get; set; } = new byte[0x14];
    public uint RootDirTableSector { get; private set; }
    public DateTime CreationTime { get; private set; }
    private byte[] Id2 { get; set; } = new byte[0x14];

    /// <summary>
    /// Private constructor to read descriptor from a specific sector using IsoSt.
    /// </summary>
    private VolumeDescriptor(IsoSt isoSt, uint sector, long byteOffset)
    {
        Sector = sector; // Store the sector we're reading from

        isoSt.ExecuteLocked(reader =>
        {
            // Calculate absolute position including the global offset (byteOffset)
            var sectorStart = byteOffset + (long)sector * IsoSt.SectorSize;

            // First, check if we can even read the full descriptor
            if (sectorStart + 0x800 > reader.BaseStream.Length)
            {
                throw new EndOfStreamException("Not enough data for volume descriptor");
            }

            // Seek to the start of the volume descriptor sector
            reader.BaseStream.Seek(sectorStart, SeekOrigin.Begin);

            // Read first magic ID (20 bytes)
            Id1 = reader.ReadBytes(0x14);
            if (Id1.Length < 0x14)
            {
                throw new EndOfStreamException("Couldn't read first magic ID");
            }

            // Read metadata
            RootDirTableSector = reader.ReadUInt32();
            reader.ReadUInt32();
            var fileTime = reader.ReadInt64();
            CreationTime = DateTime.FromFileTime(fileTime);

            // Seek to second magic ID position
            var secondMagicPos = sectorStart + 0x7EC;
            if (secondMagicPos + 0x14 > reader.BaseStream.Length)
            {
                throw new EndOfStreamException("Couldn't seek to second magic ID");
            }

            reader.BaseStream.Seek(secondMagicPos, SeekOrigin.Begin);

            // Read second magic ID (20 bytes)
            Id2 = reader.ReadBytes(0x14);
            if (Id2.Length < 0x14)
            {
                throw new EndOfStreamException("Couldn't read second magic ID");
            }
        });
    }

    // Add this method to verify the ISO format
    public bool IsRebuiltXisoFormat()
    {
        return Sector == 0;
    }

    /// <summary>
    /// Reads the volume descriptor from the stream using IsoSt.
    /// Replicates logic from xbox-iso-vfs:
    /// 1. Try Sector 32 (Offset 0)
    /// 2. Try Sector 32 (Offset GamePartitionOffset)
    /// 3. Try Sector 0 (Offset 0) - Common XISO fallback
    /// </summary>
    public static VolumeDescriptor ReadFrom(IsoSt isoSt)
    {
        Exception? firstException = null;

        // 1. Try standard sector 32, Offset 0
        try
        {
            var descriptor = new VolumeDescriptor(isoSt, VolumeDescriptorSector, 0);
            if (descriptor.Validate())
            {
                isoSt.VolumeOffset = 0;
                return descriptor;
            }
        }
        catch (Exception ex)
        {
            firstException = ex;
            DebugLogger.WriteLine($"Error reading volume descriptor from sector 32 (Offset 0): {ex.Message}");
        }

        // 2. Try standard sector 32, Offset GamePartitionOffset (Dual Layer / Hybrid)
        try
        {
            DebugLogger.WriteLine($"Checking for Game Partition at offset {GamePartitionOffset}...");
            var descriptor = new VolumeDescriptor(isoSt, VolumeDescriptorSector, GamePartitionOffset);
            if (descriptor.Validate())
            {
                isoSt.VolumeOffset = GamePartitionOffset;
                DebugLogger.WriteLine($"Detected Game Partition at offset {GamePartitionOffset}");
                return descriptor;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.WriteLine($"Error reading volume descriptor from sector 32 (Offset {GamePartitionOffset}): {ex.Message}");
        }

        // 3. Try rebuilt XISO format at sector 0, Offset 0
        try
        {
            DebugLogger.WriteLine("Checking for rebuilt XISO format at sector 0...");
            var descriptor = new VolumeDescriptor(isoSt, 0, 0);
            if (descriptor.Validate())
            {
                isoSt.VolumeOffset = 0;
                return descriptor;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.WriteLine($"Error reading volume descriptor from sector 0: {ex.Message}");

            if (firstException != null)
            {
                throw new AggregateException(
                    "Failed to read volume descriptor from all known locations",
                    firstException,
                    ex
                );
            }

            throw;
        }

        // If we reach here, sectors were readable but failed validation
        throw new InvalidImageException(
            "Volume descriptor not found at sector 0 or 32 (including game partition offset). " +
            "This doesn't appear to be a valid Xbox ISO file."
        );
    }

    public bool Validate()
    {
        // DebugLogger.WriteLine($"Validating descriptor - ID1: {BitConverter.ToString(Id1)}");
        // DebugLogger.WriteLine($"Validating descriptor - ID2: {BitConverter.ToString(Id2)}");
        return Id1.SequenceEqual(MagicId) && Id2.SequenceEqual(MagicId);
    }
}