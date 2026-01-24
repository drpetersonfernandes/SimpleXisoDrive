using System.Diagnostics;
using System.Reflection;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;
using SimpleXisoDrive.Services;

namespace SimpleXisoDrive;

public static partial class UpdateChecker
{
    private const string RepoOwner = "drpetersonfernandes";
    private const string RepoName = "SimpleXisoDrive";
    private const string LatestApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";

    private static readonly HttpClient Http;

    static UpdateChecker()
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.None
            }
        };

        Http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public static async Task CheckForUpdateAsync()
    {
        try
        {
            if (!Http.DefaultRequestHeaders.Contains("User-Agent"))
                Http.DefaultRequestHeaders.Add("User-Agent", $"{RepoName}-UpdateChecker");

            using var resp = await Http.GetAsync(LatestApiUrl);
            if (!resp.IsSuccessStatusCode) return;

            await using var jsonStream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(jsonStream);

            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();
            if (tagName is null || htmlUrl is null) return;

            var m = MyRegex().Match(tagName);
            if (!m.Success) return;

            var latest = Version.Parse(m.Value);
            var current = Assembly.GetExecutingAssembly().GetName().Version
                          ?? new Version(0, 0, 0, 0);

            if (latest <= current) return;

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"A newer version of {RepoName} is available:");
            Console.ResetColor();
            Console.WriteLine($"  Current : {current}");
            Console.WriteLine($"  Latest  : {latest}");
            Console.Write("Open the release page in your browser? [Y/n] ");

            if (Console.IsInputRedirected) return;

            var key = Console.ReadKey(true).KeyChar;
            Console.WriteLine();

            if (key is 'n' or 'N')
                return;

            try
            {
                ProcessStartInfo psi = new()
                {
                    FileName = htmlUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
                Console.WriteLine("Browser opened to latest release page.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not launch browser automatically: {ex.Message}");
                Console.WriteLine($"You can open the page manually: {htmlUrl}");
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: log and continue
            await ErrorLogger.LogErrorAsync(ex, "UpdateChecker.CheckForUpdateAsync");
        }
    }

    [GeneratedRegex(@"\d+\.\d+\.\d+")]
    private static partial Regex MyRegex();
}
