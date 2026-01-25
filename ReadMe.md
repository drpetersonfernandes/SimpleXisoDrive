# Simple Xiso Drive for Windows

Simple Xiso Drive is a lightweight utility that allows you to mount original Xbox ISO files (`.iso`) as virtual drives or NTFS directory mount points. Built on the DokanNet library, it provides high-performance, read-only access to Xbox Disc Video File System (XDVDFS) contents directly from Windows Explorer.

The application is designed for extreme memory efficiency and now supports both **Windows x64** and **Windows ARM64** architectures.

## Features

*   **Multi-Architecture Support:** Native executables for `win-x64` and `win-arm64`.
*   **Broad Format Support:** Handles standard Xbox ISO dumps (Sector 32), rebuilt "XISO" formats (Sector 0), and Dual-Layer/Hybrid discs (Game Partition offsets).
*   **Zero-Config Mounting:** Drag-and-drop an ISO onto the executable to automatically mount it to the first available drive letter (M: through R:).
*   **NTFS Integration:** Mount ISOs as drive letters (e.g., `Z:`) or into empty NTFS folders.
*   **Automated Bug Reporting:** Includes a built-in telemetry system that securely reports filesystem crashes to the developer via the PureLogic Code API.
*   **Update Checker:** Automatically checks for newer versions on GitHub to ensure you have the latest compatibility fixes.
*   **Read-Only Safety:** Ensures the source ISO remains unmodified.

## Prerequisites

1.  **.NET Runtime:** Requires the **.NET 10.0 Desktop Runtime**.
2.  **Dokan Library:** You must install the Dokan user-mode file system library (version 2.x.x).
    *   Download: [https://github.com/dokan-dev/dokany/releases](https://github.com/dokan-dev/dokany/releases).

## How to Use

### 1. Drag-and-Drop (Easiest)
*   Drag your `.iso` file and drop it onto `SimpleXisoDrive.exe` (or `SimpleXisoDrive_arm64.exe`).
*   The app will automatically find an available drive letter, mount the ISO, and open Windows Explorer.
*   **To Unmount:** Return to the console window and press any key.

### 2. Command-Line
Run the application from a terminal for specific mount points:

```shell
SimpleXisoDrive.exe <PathToIsoFile> <MountPoint> [options]
```

**Arguments:**
*   `<PathToIsoFile>`: Full path to the `.iso` file.
*   `<MountPoint>`: A drive letter (e.g., `Z:`) or a path to an empty NTFS folder.

**Options:**
*   `-l`, `--launch`: Automatically opens Windows Explorer to the mount point.
*   `-d`, `--debug`: Enables verbose Dokan debug output in the console.

## Technical Details

*   **XDVDFS Parsing:** Correcty traverses the Xbox-specific binary tree structure.
*   **Cycle Detection:** Includes safety checks to prevent infinite loops in corrupted or malformed ISO images.
*   **Mount Sanitization:** Automatically handles mount point strings (e.g., converts `Z:\` to `Z:`) to satisfy Dokan driver requirements.
*   **Smart Permissions:** Automatically adjusts Dokan options based on Administrator privileges to ensure the highest success rate for mounting.

## Troubleshooting

*   **Administrator Privileges:** While the tool attempts to mount in user-mode, mounting a global drive letter often requires Administrator rights. If the mount fails, right-click the `.exe` and select "Run as Administrator."
*   **Dokan Errors:** If you see "Dokan driver not found," ensure you have restarted your computer after installing the Dokan library.
*   **Invalid Magic String:** If the app reports "XDVDFS magic string not found," the file is likely a standard PC ISO or an encrypted Redump-style image that has not been processed for XISO compatibility.

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