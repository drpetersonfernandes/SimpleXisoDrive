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
        // Hook global exception handlers immediately to catch crashes
        SetupGlobalExceptionHandlers();

        DebugLogger.WriteLine("=== SimpleXisoDrive Started ===");
        DebugLogger.WriteLine($"Arguments: {string.Join(" | ", args)}");
        DebugLogger.WriteLine($"Working Directory: {Environment.CurrentDirectory}");

        IsDokanInstalled();

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
                    if (isoPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                        throw new ArgumentException("Invalid path characters detected");

                    var availableMountPath = FindAvailableDriveLetter();
                    if (availableMountPath is null)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        await Console.Error.WriteLineAsync("Error: Could not find an available drive letter (M-R).");
                        Console.ResetColor();
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

            if (!File.Exists(isoPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                var errorMsg = $"ISO file not found at '{isoPath}'";
                await Console.Error.WriteLineAsync($"Error: {errorMsg}");
                Console.ResetColor();

                // Report this to the API so the developer knows the path was invalid
                await ErrorLogger.LogErrorAsync(new FileNotFoundException(errorMsg), "Mount attempt failed: File not found.");

                if (!isDragAndDrop) return 1;

                DebugLogger.WriteLine("\nPress any key to exit.");
                Console.ReadKey();
                return 1;
            }

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
        catch (VfsContainer.InvalidImageException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            Console.ResetColor();
            await ErrorLogger.LogErrorAsync(ex, "Invalid ISO image specified.");
            if (!isDragAndDrop) return 1;

            DebugLogger.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
            return 1;
        }
        catch (DokanException ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            await Console.Error.WriteLineAsync($"Dokan Error: {ex.Message}");
            Console.ResetColor();
            await ErrorLogger.LogErrorAsync(ex, "A Dokan-specific error occurred during mounting.");
            if (!isDragAndDrop) return 1;

            DebugLogger.WriteLine("\nPress any key to exit.");
            Console.ReadKey();

            return 1;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            Console.ResetColor();

            await ErrorLogger.LogErrorAsync(ex, "Fatal error in Main");

            // Always wait for key press if drag-and-drop
            if (isDragAndDrop || args.Length == 1)
            {
                DebugLogger.WriteLine("\nPress any key to exit.");
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
                ErrorLogger.LogFatalException(ex, "CRITICAL: Unhandled Global Exception");
            }
        };

        // Catches exceptions thrown in background Tasks that were not awaited
        TaskScheduler.UnobservedTaskException += static (_, e) =>
        {
            ErrorLogger.LogFatalException(e.Exception, "CRITICAL: Unobserved Task Exception");
            e.SetObserved();
        };
    }

    private static void IsDokanInstalled()
    {
        try
        {
            // Try to load the Dokan driver DLL
            var dokanDllPath = Path.Combine(
                Environment.SystemDirectory,
                "drivers",
                "dokan2.sys");

            File.Exists(dokanDllPath);
        }
        catch
        {
            // ignored
        }
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
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("WARNING: Administrator privileges are recommended for mounting drive letters.");
            Console.WriteLine("If mounting fails, try running as Administrator.");
            Console.ResetColor();
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
                    await ErrorLogger.LogErrorAsync(ex, $"Failed to launch explorer at '{mountPath}'.");
                }
            }

            var tcs = new TaskCompletionSource();
            await using (CancellationTokenSource.Token.Register(() => tcs.SetResult()))
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
}