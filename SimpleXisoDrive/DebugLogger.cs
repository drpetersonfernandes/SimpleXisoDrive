namespace SimpleXisoDrive;

public static class DebugLogger
{
    private static readonly string DebugFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug.txt");
    private static readonly Lock LockObject = new();

    public static void WriteLine(string message)
    {
        // Write to the console
        Console.WriteLine(message);
        // Write to file
        lock (LockObject)
        {
            File.AppendAllText(DebugFilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}{Environment.NewLine}");
        }
    }
}