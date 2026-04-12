using System.Diagnostics;
using DokanNet;
using DokanNet.Logging;
using SimpleXisoDrive.Services;

namespace SimpleXisoDrive;

file static class Program
{
    private static VfsContainer? _vfsContainer;
    private static readonly CancellationTokenSource CancellationTokenSource = new();

    public static async Task<int> Main(string[] args)
    {
        // Set Green CRT theme immediately
        Console.BackgroundColor = ConsoleColor.Black;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Clear();

        // Hook global exception handlers immediately to catch crashes
        SetupGlobalExceptionHandlers();

        DebugLogger.WriteLine("=== SimpleXisoDrive Started ===");
        DebugLogger.WriteLine($"Arguments: {string.Join(" | ", args)}");
        DebugLogger.WriteLine($"Working Directory: {Environment.CurrentDirectory}");

        // Report launch statistics (fire and forget)
        StatsService.ReportLaunchAsync();

        if (!IsDokanInstalled())
        {
            DebugLogger.WriteLine("Dokan is not installed. Exiting.");
            DebugLogger.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
            return 1;
        }

        await UpdateChecker.CheckForUpdateAsync();

        // Clear previous debug log
        try
        {
            if (File.Exists("debug.txt"))
            {
                File.Delete("debug.txt");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Could not clear debug log: {ex.Message}");
        }

        var isDragAndDrop = false;
        var debug = false;

        try
        {
            string isoPath;
            string mountPath;
            bool launch; // Initialize launch to false
            switch (args.Length)
            {
                case 0:
                    PrintUsage();
                    DebugLogger.WriteLine("\nAlternatively, you can drag and drop an ISO file onto the executable to mount it automatically.");
                    DebugLogger.WriteLine("\nPress any key to exit.");
                    Console.ReadKey();
                    return 1;

                case 1:
                    isDragAndDrop = true;
                    isoPath = args[0];
                    if (string.IsNullOrEmpty(isoPath))
                        throw new ArgumentException("ISO path cannot be null or empty");
                    if (isoPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                        throw new ArgumentException("Invalid path characters detected");

                    var availableMountPath = FindAvailableDriveLetter();
                    if (availableMountPath is null)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        await Console.Error.WriteLineAsync("Error: Could not find an available drive letter (M-R).");
                        // For drag-and-drop, wait for a key press before exiting on error.
                        DebugLogger.WriteLine("\nPress any key to exit.");
                        Console.ReadKey();
                        return 1;
                    }

                    mountPath = availableMountPath;
                    launch = true;
                    break;

                default:
                    isoPath = args[0];
                    mountPath = args[1];
                    var options = new HashSet<string>(args.Skip(2), StringComparer.OrdinalIgnoreCase);
                    debug = options.Contains("-d") || options.Contains("--debug");
                    launch = options.Contains("-l") || options.Contains("--launch");
                    break;
            }

            // Try to resolve the ISO path - handle cases where user provides path without .iso extension
            var resolvedIsoPath = ResolveIsoPath(isoPath);
            if (resolvedIsoPath == null)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                var errorMsg = $"ISO file not found at '{isoPath}'";
                await Console.Error.WriteLineAsync($"Error: {errorMsg}");

                // Add hints for common mistakes
                if (!isoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                {
                    await Console.Error.WriteLineAsync($"Hint: Tried looking for '{isoPath}.iso' but that wasn't found either.");
                }

                if (args.Length > 2 && !isoPath.Contains(' '))
                {
                    await Console.Error.WriteLineAsync("Hint: If your file path contains spaces, ensure it is wrapped in \"quotes\".");
                }

                // Report this to the API so the developer knows the path was invalid
                await BugReport.LogErrorAsync(new FileNotFoundException(errorMsg), "Mount attempt failed: File not found.");

                if (!isDragAndDrop) return 1;

                DebugLogger.WriteLine("\nPress any key to exit.");
                Console.ReadKey();
                return 1;
            }

            // Use the resolved path for mounting
            isoPath = resolvedIsoPath;

            if (isDragAndDrop)
            {
                var mountTask = RunMount(isoPath, mountPath, debug, launch);

                // Wait for either the mount to fail OR the user to press a key
                var keyPressTask = Task.Run(static () =>
                {
                    try
                    {
                        return Console.ReadKey(true);
                    }
                    catch
                    {
                        return default;
                    }
                });

                var completedTask = await Task.WhenAny(mountTask, keyPressTask);

                if (completedTask == mountTask)
                {
                    // The mount task finished (likely failed) before a key was pressed.
                    // Await it to propagate the exception to the catch blocks below.
                    await mountTask;
                }
                else
                {
                    // User pressed a key first.
                    DebugLogger.WriteLine("\nUnmount key pressed. Unmounting...");
                    await CancellationTokenSource.CancelAsync();
                    await mountTask;
                }
            }
            else
            {
                // For standard command-line use, await the task directly.
                // The user will stop it with Ctrl+C.
                await RunMount(isoPath, mountPath, debug, launch);
            }

            return 0;
        }
        catch (InvalidImageException ex)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            await BugReport.LogErrorAsync(ex, "Invalid ISO image specified.");
            if (!isDragAndDrop) return 1;

            DebugLogger.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
            return 1;
        }
        catch (DokanException ex)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            await Console.Error.WriteLineAsync($"Dokan Error: {ex.Message}");
            await BugReport.LogErrorAsync(ex, "A Dokan-specific error occurred during mounting.");
            if (!isDragAndDrop) return 1;

            DebugLogger.WriteLine("\nPress any key to exit.");
            Console.ReadKey();

            return 1;
        }
        catch (DllNotFoundException ex) when (ex.Message.Contains("dokan2.dll", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Error.WriteLine("Error: Failed to load the Dokan runtime library (dokan2.dll).");
            Console.Error.WriteLine("The file may be corrupted, of the wrong architecture, or its dependencies are missing.");
            Console.Error.WriteLine("");
            Console.Error.WriteLine("To fix this:");
            Console.Error.WriteLine("  1. Uninstall Dokan via Windows Settings > Apps");
            Console.Error.WriteLine("  2. Download the latest version from: https://github.com/dokan-dev/dokany/releases");
            Console.Error.WriteLine("  3. Install the package matching your system architecture (x64)");
            Console.Error.WriteLine("  4. Restart your computer");
            Console.Error.WriteLine("  5. Re-run SimpleXisoDrive");

            await BugReport.LogErrorAsync(ex, "Unable to load dokan2.dll or its dependencies.");
            if (!isDragAndDrop) return 1;

            DebugLogger.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
            return 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");

            await BugReport.LogErrorAsync(ex, "Fatal error in Main");

            // If we are in a context where the window might disappear (Drag & Drop or single arg)
            if (isDragAndDrop || args.Length <= 1)
            {
                Console.WriteLine("\nPress any key to exit.");
                Console.ReadKey();
            }

            return 1;
        }
    }

    private static void SetupGlobalExceptionHandlers()
    {
        // Catches exceptions thrown on the main thread that are not caught
        AppDomain.CurrentDomain.UnhandledException += static (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                BugReport.LogFatalException(ex, "CRITICAL: Unhandled Global Exception");
            }
        };

        // Catches exceptions thrown in background Tasks that were not awaited
        TaskScheduler.UnobservedTaskException += static (_, e) =>
        {
            BugReport.LogFatalException(e.Exception, "CRITICAL: Unobserved Task Exception");
            e.SetObserved();
        };
    }

    /// <summary>
    /// Checks whether the Dokan runtime library (dokan2.dll) and driver (dokan2.sys) are installed.
    /// Displays an error and exits if dokan2.dll is missing, since the application cannot function without it.
    /// </summary>
    /// <returns>True if dokan2.dll is found; false otherwise.</returns>
    private static bool IsDokanInstalled()
    {
        var dokanDllPath = Path.Combine(Environment.SystemDirectory, "dokan2.dll");
        var dokanSysPath = Path.Combine(Environment.SystemDirectory, "drivers", "dokan2.sys");

        var dllExists = File.Exists(dokanDllPath);
        var sysExists = File.Exists(dokanSysPath);

        if (!dllExists)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Error.WriteLine("Error: The Dokan runtime library (dokan2.dll) was not found.");
            Console.Error.WriteLine("");
            Console.Error.WriteLine("SimpleXisoDrive requires the Dokan User-Mode File System Library to operate.");
            Console.Error.WriteLine("");
            Console.Error.WriteLine("To fix this:");
            Console.Error.WriteLine("  1. Download Dokan from: https://github.com/dokan-dev/dokany/releases");
            Console.Error.WriteLine("  2. Install the package (the default installation includes dokan2.dll)");
            Console.Error.WriteLine("  3. Restart your computer if prompted");
            Console.Error.WriteLine("  4. Re-run SimpleXisoDrive");
            Console.Error.WriteLine("");
            Console.Error.WriteLine($"Expected file location: {dokanDllPath}");

            DebugLogger.WriteLine($"Dokan check FAILED: {dokanDllPath} not found.");
            return false;
        }

        if (!sysExists)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Error.WriteLine("Warning: The Dokan driver (dokan2.sys) was not found.");
            Console.Error.WriteLine("Mounting may fail. Please reinstall Dokan if you encounter issues.");
            DebugLogger.WriteLine($"Dokan driver warning: {dokanSysPath} not found.");
        }

        DebugLogger.WriteLine($"Dokan check passed: {dokanDllPath} found.");
        return true;
    }

    private static string? FindAvailableDriveLetter()
    {
        try
        {
            // Get all existing drive letters
            var usedLetters = DriveInfo.GetDrives()
                .Select(static d => d.Name[0])
                .ToHashSet();

            char[] preferredLetters = ['M', 'N', 'O', 'P', 'Q', 'R'];

            foreach (var letter in preferredLetters)
            {
                if (!usedLetters.Contains(letter))
                {
                    var drivePath = $"{letter}:\\";
                    DebugLogger.WriteLine($"Found available drive letter: {drivePath}");
                    return drivePath;
                }
            }

            DebugLogger.WriteLine("No available drive letters found in preferred range M-R");
            return null;
        }
        catch (Exception ex)
        {
            DebugLogger.WriteLine($"Error checking drive letters: {ex.Message}");
            return null;
        }
    }

    private static void PrintUsage()
    {
        var mainModule = Process.GetCurrentProcess().MainModule;
        var exeName = mainModule != null
            ? Path.GetFileNameWithoutExtension(mainModule.FileName)
            : "SimpleXisoDrive";
        DebugLogger.WriteLine("Mounts an Xbox ISO file as a virtual file system on Windows.");
        DebugLogger.WriteLine("");
        DebugLogger.WriteLine($"Usage: {exeName} <iso-file> <mount-path> [options]");
        DebugLogger.WriteLine("");
        DebugLogger.WriteLine("Arguments:");
        DebugLogger.WriteLine("  <iso-file>      Path to the Xbox ISO file to mount.");
        DebugLogger.WriteLine("  <mount-path>    Drive letter (\"M:\\\") or folder path on an NTFS partition.");
        DebugLogger.WriteLine("");
        DebugLogger.WriteLine("Options:");
        DebugLogger.WriteLine("  -d, --debug     Display debug Dokan output in the console window.");
        DebugLogger.WriteLine("  -l, --launch    Open Windows Explorer to the mount path after mounting.");
    }

    private static async Task RunMount(string isoPath, string mountPath, bool debug, bool launch)
    {
        // Check for admin rights for drive letter mounting
        if (mountPath.EndsWith(":\\", StringComparison.Ordinal) && !CheckAccess.IsAdministrator())
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("WARNING: Administrator privileges are recommended for mounting drive letters.");
            Console.WriteLine("If mounting fails, try running as Administrator.");
            DebugLogger.WriteLine("Running without administrator privileges");
        }

        Console.CancelKeyPress += static (_, e) =>
        {
            e.Cancel = true;
            DebugLogger.WriteLine("Ctrl+C detected. Unmounting...");
            CancellationTokenSource.Cancel();
        };

        try
        {
            DebugLogger.WriteLine($"Attempting to mount '{isoPath}' to '{mountPath}'...");

            // Dokan fails if a drive letter has a trailing backslash (e.g. "Z:\" fails, "Z:" works)
            if (mountPath.Length == 3 && mountPath.EndsWith(":\\", StringComparison.Ordinal))
            {
                mountPath = mountPath.Substring(0, 2);
            }

            _vfsContainer = new VfsContainer(isoPath);

            // Use MountManager only if we have Admin rights, otherwise it often fails with "Something's wrong with the Dokan driver"
            var dokanOptions = DokanOptions.WriteProtection | DokanOptions.CurrentSession;

            if (CheckAccess.IsAdministrator())
            {
                dokanOptions |= DokanOptions.MountManager;
            }

            if (debug)
            {
                dokanOptions |= DokanOptions.DebugMode | DokanOptions.StderrOutput;
            }

            var dokan = new Dokan(new ConsoleLogger("[Dokan] "));
            var dokanBuilder = new DokanInstanceBuilder(dokan)
                .ConfigureOptions(options =>
                {
                    options.Options = dokanOptions;
                    options.MountPoint = mountPath;
                });

            using var dokanInstance = dokanBuilder.Build(new XboxIsoVfsDokan(_vfsContainer));

            DebugLogger.WriteLine($"Mount successful: '{isoPath}' -> '{mountPath}'");
            DebugLogger.WriteLine("Press Ctrl+C to unmount (if run from command line).");

            if (launch)
            {
                try
                {
                    Process.Start("explorer.exe", mountPath);
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteLine($"Failed to open Windows Explorer: {ex.Message}");
                    await BugReport.LogErrorAsync(ex, $"Failed to launch explorer at '{mountPath}'.");
                }
            }

            var tcs = new TaskCompletionSource();
            await using (CancellationTokenSource.Token.Register(tcs.SetResult))
            {
                await tcs.Task;
            }

            DebugLogger.WriteLine("Unmount signal received. Cleaning up...");
        }
        catch (Exception ex)
        {
            DebugLogger.WriteLine($"Mount process failed: {ex.Message}");
            throw; // Re-throw so Main can handle the UI/Console feedback
        }
        finally
        {
            _vfsContainer?.Dispose();
            DebugLogger.WriteLine("Unmounted.");
        }
    }

    /// <summary>
    /// Resolves the ISO file path, handling cases where the user provides a path without the .iso extension.
    /// Tries multiple strategies to find the file:
    /// 1. Return original path if file exists
    /// 2. If no extension, try appending .iso
    /// 3. If just a filename, try looking in current directory
    /// </summary>
    private static string? ResolveIsoPath(string isoPath)
    {
        // 1. Check if the file exists as-is
        if (File.Exists(isoPath))
        {
            return isoPath;
        }

        // 2. If no extension provided, try appending .iso
        if (string.IsNullOrEmpty(Path.GetExtension(isoPath)))
        {
            var withExtension = isoPath + ".iso";
            if (File.Exists(withExtension))
            {
                DebugLogger.WriteLine($"Resolved '{isoPath}' to '{withExtension}'");
                return withExtension;
            }
        }

        // 3. If it's just a filename (no path), try looking in current directory
        if (!isoPath.Contains(Path.DirectorySeparatorChar) && !isoPath.Contains(Path.AltDirectorySeparatorChar))
        {
            var inCurrentDir = Path.Combine(Environment.CurrentDirectory, isoPath);
            if (File.Exists(inCurrentDir))
            {
                DebugLogger.WriteLine($"Resolved '{isoPath}' to '{inCurrentDir}'");
                return inCurrentDir;
            }

            // Also try with .iso extension in current directory
            if (string.IsNullOrEmpty(Path.GetExtension(isoPath)))
            {
                var inCurrentDirWithExt = inCurrentDir + ".iso";
                if (File.Exists(inCurrentDirWithExt))
                {
                    DebugLogger.WriteLine($"Resolved '{isoPath}' to '{inCurrentDirWithExt}'");
                    return inCurrentDirWithExt;
                }
            }
        }

        // File not found
        return null;
    }
}