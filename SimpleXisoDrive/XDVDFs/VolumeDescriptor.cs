using SimpleXisoDrive.Services;

namespace SimpleXisoDrive.XDVDFs;

public class VolumeDescriptor
{
    public uint Sector { get; }
    private const int VolumeDescriptorSector = 32;

    // Offset used in xbox-iso-vfs for dual layer / game partitions (XGD1)
    // 2048 * 32 * 6192 = 405,798,912 bytes
    private const long Xgd1PartitionOffset = 2048L * 32 * 6192;

    // XGD3 partition offset (from extract-xiso.c: XGD3_LSEEK_OFFSET)
    private const long Xgd3PartitionOffset = 0x02080000L; // 34,078,720 bytes

    // GLOBAL partition offset (from extract-xiso.c: GLOBAL_LSEEK_OFFSET)
    private const long GlobalPartitionOffset = 0x0FD90000L; // 265,879,552 bytes

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
            try
            {
                CreationTime = DateTime.FromFileTime(fileTime);
            }
            catch
            {
                CreationTime = DateTime.MinValue;
            }

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
    /// Replicates logic from extract-xiso.c:
    /// 1. Try Sector 32 (Offset 0) - Standard Xbox
    /// 2. Try Sector 32 (Offset GlobalPartitionOffset) - GLOBAL format
    /// 3. Try Sector 32 (Offset Xgd3PartitionOffset) - XGD3 format
    /// 4. Try Sector 32 (Offset Xgd1PartitionOffset) - XGD1 Dual Layer
    /// 5. Try Sector 0 (Offset 0) - Common XISO fallback
    /// </summary>
    public static VolumeDescriptor ReadFrom(IsoSt isoSt)
    {
        Exception? firstException = null;
        var errors = new List<string>();
        var fileSize = isoSt.Reader.BaseStream.Length;

        // 1. Try standard sector 32, Offset 0
        try
        {
            var descriptor = new VolumeDescriptor(isoSt, VolumeDescriptorSector, 0);
            if (descriptor.Validate())
            {
                isoSt.VolumeOffset = 0;
                return descriptor;
            }

            errors.Add("Sector 32 (Offset 0): Found data but magic ID mismatch (not a valid XDVDFS signature)");
        }
        catch (Exception ex)
        {
            firstException = ex;
            var errorDetail = ex is EndOfStreamException ? "file too small" : ex.Message;
            errors.Add($"Sector 32 (Offset 0): {errorDetail}");
            DebugLogger.WriteLine($"Error reading volume descriptor from sector 32 (Offset 0): {ex.Message}");
        }

        // 2. Try Sector 32, Offset GlobalPartitionOffset (GLOBAL format)
        try
        {
            DebugLogger.WriteLine($"Checking for Global Partition at offset {GlobalPartitionOffset}...");
            var descriptor = new VolumeDescriptor(isoSt, VolumeDescriptorSector, GlobalPartitionOffset);
            if (descriptor.Validate())
            {
                isoSt.VolumeOffset = GlobalPartitionOffset;
                DebugLogger.WriteLine($"Detected Global Partition at offset {GlobalPartitionOffset}");
                return descriptor;
            }

            errors.Add($"Sector 32 (Offset {GlobalPartitionOffset}): Found data but magic ID mismatch (not a valid global partition)");
        }
        catch (Exception ex)
        {
            var errorDetail = ex is EndOfStreamException ? "file too small for global partition" : ex.Message;
            errors.Add($"Sector 32 (Offset {GlobalPartitionOffset}): {errorDetail}");
            DebugLogger.WriteLine($"Error reading volume descriptor from sector 32 (Offset {GlobalPartitionOffset}): {ex.Message}");
        }

        // 3. Try Sector 32, Offset Xgd3PartitionOffset (XGD3 format)
        try
        {
            DebugLogger.WriteLine($"Checking for XGD3 Partition at offset {Xgd3PartitionOffset}...");
            var descriptor = new VolumeDescriptor(isoSt, VolumeDescriptorSector, Xgd3PartitionOffset);
            if (descriptor.Validate())
            {
                isoSt.VolumeOffset = Xgd3PartitionOffset;
                DebugLogger.WriteLine($"Detected XGD3 Partition at offset {Xgd3PartitionOffset}");
                return descriptor;
            }

            errors.Add($"Sector 32 (Offset {Xgd3PartitionOffset}): Found data but magic ID mismatch (not a valid XGD3 partition)");
        }
        catch (Exception ex)
        {
            var errorDetail = ex is EndOfStreamException ? "file too small for XGD3 partition" : ex.Message;
            errors.Add($"Sector 32 (Offset {Xgd3PartitionOffset}): {errorDetail}");
            DebugLogger.WriteLine($"Error reading volume descriptor from sector 32 (Offset {Xgd3PartitionOffset}): {ex.Message}");
        }

        // 4. Try Sector 32, Offset Xgd1PartitionOffset (XGD1 Dual Layer / Hybrid)
        try
        {
            DebugLogger.WriteLine($"Checking for XGD1 Game Partition at offset {Xgd1PartitionOffset}...");
            var descriptor = new VolumeDescriptor(isoSt, VolumeDescriptorSector, Xgd1PartitionOffset);
            if (descriptor.Validate())
            {
                isoSt.VolumeOffset = Xgd1PartitionOffset;
                DebugLogger.WriteLine($"Detected XGD1 Game Partition at offset {Xgd1PartitionOffset}");
                return descriptor;
            }

            errors.Add($"Sector 32 (Offset {Xgd1PartitionOffset}): Found data but magic ID mismatch (not a valid XGD1 partition)");
        }
        catch (Exception ex)
        {
            var errorDetail = ex is EndOfStreamException ? "file too small for XGD1 partition" : ex.Message;
            errors.Add($"Sector 32 (Offset {Xgd1PartitionOffset}): {errorDetail}");
            DebugLogger.WriteLine($"Error reading volume descriptor from sector 32 (Offset {Xgd1PartitionOffset}): {ex.Message}");
        }

        // 5. Try rebuilt XISO format at sector 0, Offset 0
        try
        {
            DebugLogger.WriteLine("Checking for rebuilt XISO format at sector 0...");
            var descriptor = new VolumeDescriptor(isoSt, 0, 0);
            if (descriptor.Validate())
            {
                isoSt.VolumeOffset = 0;
                return descriptor;
            }

            errors.Add("Sector 0 (Offset 0): Found data but magic ID mismatch (not a valid XISO)");
        }
        catch (Exception ex)
        {
            var errorDetail = ex is EndOfStreamException ? "file too small" : ex.Message;
            errors.Add($"Sector 0 (Offset 0): {errorDetail}");
            DebugLogger.WriteLine($"Error reading volume descriptor from sector 0: {ex.Message}");

            if (firstException != null)
            {
                var aggregateEx = new AggregateException(
                    "Failed to read volume descriptor from all known locations",
                    firstException,
                    ex
                );
                throw aggregateEx;
            }

            throw;
        }

        // If we reach here, sectors were readable but failed validation
        // Provide detailed diagnostic information
        var fileSizeInfo = $"File size: {fileSize:N0} bytes ({fileSize / 1024.0 / 1024.0:F2} MB)";
        var errorDetails = string.Join("\n  - ", errors);

        throw new InvalidImageException(
            $"Volume descriptor not found. This doesn't appear to be a valid Xbox ISO file.\n\n" +
            $"{fileSizeInfo}\n\n" +
            $"Tried the following locations:\n  - {errorDetails}\n\n" +
            $"Possible causes:\n" +
            $"  - The file is not an Xbox ISO (may be a different format)\n" +
            $"  - The ISO is corrupted or incomplete\n" +
            $"  - The ISO uses an unsupported format variant\n\n" +
            $"Expected magic ID: {System.Text.Encoding.ASCII.GetString(MagicId)}"
        );
    }

    public bool Validate()
    {
        // DebugLogger.WriteLine($"Validating descriptor - ID1: {BitConverter.ToString(Id1)}");
        // DebugLogger.WriteLine($"Validating descriptor - ID2: {BitConverter.ToString(Id2)}");
        return Id1.SequenceEqual(MagicId) && Id2.SequenceEqual(MagicId);
    }
}