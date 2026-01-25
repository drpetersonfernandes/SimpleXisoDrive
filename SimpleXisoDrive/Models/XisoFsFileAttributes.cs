namespace SimpleXisoDrive.Models;

/// <summary>
/// Represents attributes associated with a file or directory in an XISO filesystem.
/// </summary>
/// <remarks>
/// This enumeration defines file attributes specific to the XISO filesystem,
/// which are represented as a single byte and may include flags for common file properties
/// such as read-only, hidden, system, directory, etc. Multiple attributes can be combined
/// using a bitwise OR operation due to the [Flags] attribute on the enum.
/// </remarks>
[Flags]
public enum XisoFsFileAttributes : byte
{
    /// <summary>
    /// Represents the "ReadOnly" file attribute in the Xiso filesystem.
    /// This attribute indicates that a file is read-only and cannot be modified.
    /// It is primarily used to protect the file from being altered or deleted.
    /// </summary>
    ReadOnly = 0x01,

    /// <summary>
    /// Represents a file attribute indicating that the file or directory is hidden.
    /// Hidden items are not typically visible in standard directory listings,
    /// unless a request is explicitly made to include hidden items.
    /// </summary>
    Hidden = 0x02,

    /// <summary>
    /// Represents the "System" file attribute within the XISO file system.
    /// This attribute is used to indicate that a file or directory is a critical component
    /// of the system and may require elevated permissions or special handling.
    /// </summary>
    System = 0x04,

    /// <summary>
    /// Represents a directory attribute in the Xiso file system.
    /// </summary>
    Directory = 0x10,

    /// <summary>
    /// Indicates that the file or directory is marked for backup or archival purposes.
    /// This attribute is commonly used to identify files or directories that have been
    /// modified since the last backup.
    /// </summary>
    Archive = 0x20,

    /// <summary>
    /// Represents a normal file attribute in the XISO file system.
    /// This attribute indicates a standard file without any special properties.
    /// </summary>
    Normal = 0x80
}