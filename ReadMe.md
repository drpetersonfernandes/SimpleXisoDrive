# Simple Xiso Drive for Windows

Simple Xiso Drive is a lightweight utility that allows you to mount original Xbox ISO files (`.iso`) as virtual drives or NTFS directory mount points. Built on the DokanNet library, it provides high-performance, read-only access to Xbox Disc Video File System (XDVDFS) contents directly from Windows Explorer.

The application is designed for extreme memory efficiency. It loads the file/directory structure into RAM on startup but streams actual file data directly from the ISO on demand. This allows it to handle multi-gigabyte ISOs with negligible memory overhead.

## Features

*   **Broad Format Support:** Handles standard Xbox ISO dumps (Sector 32), rebuilt "XISO" formats (Sector 0), and Dual-Layer/Hybrid discs (Game Partition offsets).
*   **Zero-Config Mounting:** Drag-and-drop an ISO onto the executable to automatically mount it to the first available drive letter (M: through R:).
*   **NTFS Integration:** Mount ISOs as drive letters (e.g., `M:\`) or into empty NTFS folders.
*   **Automated Bug Reporting:** Includes a built-in error logging system that securely reports crashes to the developer to improve compatibility.
*   **Update Checker:** Automatically checks for newer versions on GitHub to ensure you are using the latest improvements.
*   **Read-Only Safety:** Ensures the source ISO remains unmodified.
*   **High Performance:** Uses asynchronous I/O and optimized binary tree traversal for fast directory browsing.

## Prerequisites

1.  **.NET Runtime:** This application requires the **.NET 10.0 Desktop Runtime**.
2.  **Dokan Library:** You must install the Dokan user-mode file system library.
    *   Download the latest version (2.x.x) from: [https://github.com/dokan-dev/dokany/releases](https://github.com/dokan-dev/dokany/releases).

## How to Use

### 1. Drag-and-Drop (Easiest)
*   Drag your `.iso` file and drop it onto `SimpleXisoDrive.exe`.
*   The app will find an available drive letter (starting at M:), mount the ISO, and open Windows Explorer automatically.
*   **To Unmount:** Return to the console window and press any key.

### 2. Command-Line
Run the application from a terminal for specific mount points:

```shell
SimpleXisoDrive.exe <PathToIsoFile> <MountPoint> [options]
```

**Arguments:**
*   `<PathToIsoFile>`: Full path to the `.iso`.
*   `<MountPoint>`: A drive letter (e.g., `X:\`) or an empty NTFS folder path.

**Options:**
*   `-l`, `--launch`: Automatically opens Windows Explorer to the mount point.
*   `-d`, `--debug`: Enables verbose Dokan debug output in the console.

## Technical Details

*   **XDVDFS Parsing:** The app correctly traverses the Xbox-specific binary tree structure used for directory entries.
*   **Cycle Detection:** Includes safety checks to prevent infinite loops in corrupted or malformed ISO images.
*   **Telemetry:** If a filesystem error occurs, the app logs the exception details to `error.log` and attempts to send a bug report to `purelogiccode.com` to help the developer fix the issue. No private file data is ever transmitted.

## Troubleshooting

*   **Administrator Privileges:** Mounting a drive letter often requires Administrator rights. If the mount fails, right-click the `.exe` and select "Run as Administrator."
*   **Dokan Errors:** If you see "Dokan driver not found," ensure you have restarted your computer after installing the Dokan library.
*   **Invalid Magic String:** If the app reports "XDVDFS magic string not found," the file is likely a standard ISO9660 (PC) image or an encrypted/redump-style image that has not been processed for XISO compatibility.
*   **Debug Log:** Check `debug.txt` in the application folder for a step-by-step log of the mounting process.

## Support the Project

If you find this tool useful, consider supporting development:
*   **Star the Repo:** [GitHub Repository](https://github.com/drpetersonfernandes/SimpleXisoDrive)
*   **Donate:** [https://purelogiccode.com/Donate](https://purelogiccode.com/Donate)

## License

This project is licensed under **GPL-3.0**.
*   **DokanNet:** MIT License.
*   **Dokan Library:** LGPL/MIT.

---
*Developed by [PureLogic Code](https://purelogiccode.com/).*