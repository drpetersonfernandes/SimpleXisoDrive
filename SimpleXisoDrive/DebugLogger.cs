namespace SimpleXisoDrive;

public static class DebugLogger
{
    private static readonly string DebugFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.txt");
    private static readonly Lock LockObject = new();
    private static bool _fileLoggingEnabled;

    public static void WriteLine(string message)
    {
        try
        {
            Console.WriteLine(message);
        }
        catch
        {
            // If the console is unavailable, we can't do much
        }

        // Write to the file only if logging is enabled
        if (!_fileLoggingEnabled)
            return;

        lock (LockObject)
        {
            try
            {
                File.AppendAllText(DebugFilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                // Disable file logging if it fails to prevent repeated exceptions
                _fileLoggingEnabled = false;
                try
                {
                    Console.Error.WriteLine($"[Warning] Debug logging to file disabled: {ex.Message}");
                }
                catch
                {
                    // Console might also be unavailable
                }
            }
        }
    }
}