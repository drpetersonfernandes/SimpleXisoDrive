# Simple Xiso Drive for Windows

This application allows you to mount original Xbox ISO files (`.iso`) as virtual drives or directories on your Windows system using the DokanNet library. It provides read-only access to the contents of the ISO file as if it were a regular part of your filesystem.

It accesses the source ISO file via a file stream directly from disk. The file and directory table is loaded into memory on startup, but file data itself is streamed directly from the ISO as it is requested. This approach is extremely memory-efficient and supports very large ISO files without consuming significant RAM.

## Features

*   Mount Xbox ISO archives as virtual drives (e.g., `M:\`) or to an NTFS folder mount point (e.g., `C:\mount\myiso`).
*   **Supports both standard ISO dumps and common rebuilt "XISO" formats.**
*   **Drag-and-drop an ISO file onto the `.exe` icon to automatically mount it** to the first available drive letter from M: to R:.
*   Read-only access to the ISO contents, preserving the original file structure.
*   Extremely memory-efficient, handling ISO files of any size by streaming file data directly from disk.
*   Correctly parses the XDVDFS (Xbox Disc Video File System).
*   Handles basic file and directory information (names, sizes, timestamps).

## Prerequisites

1.  **.NET Runtime:** The application is built for .NET 9.0 (or a compatible newer version). You'll need the .NET Desktop Runtime installed.
2.  **Dokan Library:** This application depends on the Dokan user-mode file system library for Windows.
    *   Download and install the latest Dokan library from the official DokanNet GitHub releases: [https://github.com/dokan-dev/dokany/releases](https://github.com/dokan-dev/dokany/releases).

## How to Build

1.  Clone or download this repository/source code.
2.  Open the solution in Visual Studio (2022 or later recommended) or use the .NET CLI.
3.  Ensure the `DokanNet` NuGet package is restored. The provided `.csproj` file includes this dependency.
4.  Build the solution (e.g., `dotnet build -c Release`).

The executable `SimpleXisoDrive.exe` will be in the `bin\Release\net9.0-windows` (or similar) directory.

## How to Use

There are two main ways to use Simple Xiso Drive:

**1. Command-Line (Explicit Mount Point):**

Run the application from the command line specifying the ISO file and the desired mount point:

```shell
SimpleXisoDrive.exe <PathToIsoFile> <MountPoint> [options]
```

**Arguments:**

*   `<PathToIsoFile>`: The full path to the Xbox ISO file you want to mount.
    *   Example: `C:\Users\YourName\Downloads\my_game.iso`
*   `<MountPoint>`: The desired mount point. This can be:
    *   A drive letter with a trailing backslash (e.g., `M:\`, `X:\`).
    *   A full path to an existing empty NTFS directory (e.g., `C:\mount\my_virtual_iso`). The directory must exist and should ideally be empty.

**Options:**

*   `-l`, `--launch`: Open Windows Explorer to the mount path after mounting.
*   `-d`, `--debug`: Display debug Dokan output in the console window.

**2. Drag-and-Drop (Automatic Mount Point):**

*   Simply drag your `.iso` file from Windows Explorer and drop it onto the `SimpleXisoDrive.exe` icon.
*   The application will attempt to mount the ISO file automatically. It will first try to use drive letter `M:\`. If `M:\` is unavailable, it will try `N:\`, then `O:\`, `P:\`, `Q:\`, and finally `R:\`.
*   If a mount is successful, Windows Explorer will open to the new drive, and the console window will remain open, showing the active mount.
*   If all preferred drive letters (M-R) are unavailable or an error occurs, the console window will remain open displaying the error message.

**To Unmount (for both methods):**

*   Press `Ctrl+C` in the console window where `SimpleXisoDrive.exe` is running.
*   Alternatively, simply close the console window.

The application will attempt to unmount the virtual drive/directory upon exit.

## Important Notes

*   **Administrator Privileges:** Mounting to a drive letter or certain system paths might require running the application as an Administrator. If you encounter "Access Denied" or "MountPoint" errors (especially with drag-and-drop), try running `SimpleXisoDrive.exe` from an administrative command prompt.
*   **Read-Only:** This is a read-only filesystem. You cannot write, delete, or modify files within the mounted ISO.
*   **Memory Usage:**
    *   The application is very memory-efficient. It only loads the ISO's file and directory structure into RAM, which typically uses a negligible amount of memory.
    *   All file data is streamed directly from the ISO file on disk when an application requests it. No file content is cached in memory.
*   **Dokan Driver:** Ensure the Dokan driver is correctly installed and running. If you have issues, reinstalling Dokan might help.
*   **Error Handling:** The application includes basic error handling and logging to the console. If a mount fails, the console window will remain open with error details. For more detailed Dokan-level debugging, you can use the `-d` or `--debug` command-line flag.

## Troubleshooting

*   **"Dokan Error: ... MountPoint ... AssignDriveLetter ..."**:
    *   The mount point might already be in use.
    *   You might need administrator privileges (see "Important Notes").
    *   If mounting to a folder, ensure the folder exists.
    *   Ensure the Dokan driver is installed and functioning.
*   **Drag-and-Drop Fails to Mount**:
    *   All preferred drive letters (M: through R:) might be in use or require administrator privileges to access. Check the console output for specific errors.
    *   Try running `SimpleXisoDrive.exe` as an administrator first, then drag the file onto it.
*   **"Error: ISO file not found..."**: Double-check the path to your ISO file.
*   **"Error: XDVDFS magic string not found..."**: This means the file you are trying to mount is not a valid, non-encrypted original Xbox ISO file, or the file is corrupted. The program checks for both standard and rebuilt ISO formats, so if this error occurs, the file is likely invalid.
*   **Application (e.g., an emulator) fails to read files correctly:**
    *   Check the console output of `SimpleXisoDrive.exe` for any errors logged by the VFS.
    *   Use the `--debug` flag when running the application to get more detailed output from the Dokan driver itself.

## Support the Project

If you find Simple Xiso Drive useful, please consider supporting its development:

*   **Star the Repository:** Show your appreciation by starring the project on GitHub!
*   **Donate:** Contributions help cover development time and costs. You can donate at: [https://purelogiccode.com/Donate](https://purelogiccode.com/Donate)

The developer's website is [PureLogic Code](https://purelogiccode.com/).

## License

This project has a GPL-3.0 license. The DokanNet library has an MIT license. The underlying Dokan library contains LGPL and MIT licensed programs.

## Acknowledgements

*   [DokanNet](https://github.com/dokan-dev/dokan-dotnet) - .NET wrapper for Dokan
*   [Dokan](https://github.com/dokan-dev/dokany) - User-mode file system library for Windows