using System.Diagnostics;
using DokanNet;
using DokanNet.Logging;

namespace SimpleXisoDrive;

file static class Program
{
    private static VfsContainer? _vfsContainer;
    private static readonly CancellationTokenSource CancellationTokenSource = new();

    public static async Task<int> Main(string[] args)
    {
        // Clear previous debug log
        if (File.Exists("debug.txt"))
        {
            File.Delete("debug.txt");
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
                await Console.Error.WriteLineAsync($"Error: ISO file not found at '{isoPath}'");
                Console.ResetColor();
                if (!isDragAndDrop) return 1;

                DebugLogger.WriteLine("\nPress any key to exit.");
                Console.ReadKey();
                return 1;
            }

            // --- MODIFIED LOGIC ---
            if (isDragAndDrop)
            {
                // For drag-and-drop, start the mount task but don't wait for it.
                // This allows the Main method to continue and wait for user input.
                var mountTask = RunMount(isoPath, mountPath, debug, launch);

                // The "Mount successful" message is now displayed from within RunMount.
                DebugLogger.WriteLine("\nPress any key to unmount and exit.");
                Console.ReadKey(true); // Wait for a key press.

                DebugLogger.WriteLine("\nUnmount key pressed. Unmounting...");
                await CancellationTokenSource.CancelAsync(); // Signal the task to unmount.

                // Now, wait for the unmount and cleanup to complete.
                await mountTask;
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
            await Console.Error.WriteLineAsync($"An unexpected error occurred: {ex.Message}");
            Console.ResetColor();
            await ErrorLogger.LogErrorAsync(ex, "An unexpected error occurred in the main application thread.");
            if (!isDragAndDrop) return 1;

            DebugLogger.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
            return 1;
        }
        // The finally block is no longer needed as the logic is handled explicitly above.
    }

    private static string? FindAvailableDriveLetter()
    {
        char[] preferredLetters = ['M', 'N', 'O', 'P', 'Q', 'R'];
        foreach (var letter in preferredLetters)
        {
            var drivePath = $"{letter}:\\";
            if (!Directory.Exists(drivePath))
            {
                return drivePath;
            }
        }

        return null;
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
        Console.CancelKeyPress += static (_, e) =>
        {
            e.Cancel = true;
            DebugLogger.WriteLine("Ctrl+C detected. Unmounting...");
            CancellationTokenSource.Cancel();
        };

        try
        {
            DebugLogger.WriteLine($"Attempting to mount '{isoPath}' to '{mountPath}'...");
            _vfsContainer = new VfsContainer(isoPath);

            var dokanOptions = DokanOptions.WriteProtection | DokanOptions.MountManager | DokanOptions.CurrentSession;
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
            // CancellationTokenRegistration is IDisposable, not IAsyncDisposable, so 'using' is correct.
            await using (CancellationTokenSource.Token.Register(() => tcs.SetResult()))
            {
                await tcs.Task;
            }

            DebugLogger.WriteLine("Unmount signal received. Cleaning up...");
        }
        finally
        {
            _vfsContainer?.Dispose();
            DebugLogger.WriteLine("Unmounted.");
        }
    }
}
