using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Authentication;
using System.Text.Json.Serialization;

namespace SimpleXisoDrive.Services;

/// <summary>
/// Service for reporting application launch statistics to the central stats API.
/// </summary>
public static class StatsService
{
    // Base URL for the stats API - points to the local ApplicationStats service
    private const string StatsApiBaseUrl = "https://www.purelogiccode.com";
    private const string StatsEndpoint = "/ApplicationStats/stats";

    // API Key for authentication - this should match the SecretKey in ApplicationStats appsettings.json
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";

    // Application identifier for this app
    private const string ApplicationId = "simplexisodrive";

    private static readonly HttpClient Http;

    static StatsService()
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
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    /// <summary>
    /// Reports application launch statistics to the central stats API.
    /// This is a fire-and-forget operation that runs in the background and does not block the application.
    /// </summary>
    public static void ReportLaunchAsync()
    {
        // Fire and forget - don't await, don't block startup
        _ = ReportLaunchInternalAsync();
    }

    private static async Task ReportLaunchInternalAsync()
    {
        try
        {
            // Get current version from assembly
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.1.0";

            var request = new StatsRequest
            {
                AppId = ApplicationId,
                AppVersion = version
            };

            // Set authorization header
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            var response = await Http.PostAsJsonAsync(
                $"{StatsApiBaseUrl}{StatsEndpoint}",
                request);

            if (response.IsSuccessStatusCode)
            {
                DebugLogger.WriteLine("Stats reported successfully.");
            }
            else
            {
                DebugLogger.WriteLine($"Stats API returned: {response.StatusCode}");
            }
        }
        catch (TaskCanceledException)
        {
            // Timeout - stats service may not be running, ignore
            DebugLogger.WriteLine("Stats API timeout - service may not be available.");
        }
        catch (HttpRequestException ex)
        {
            // Connection failed - stats service may not be running, ignore
            DebugLogger.WriteLine($"Stats API unreachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Log but don't fail startup for stats reporting issues
            DebugLogger.WriteLine($"Failed to report stats: {ex.Message}");
        }
    }

    /// <summary>
    /// Request model for the stats API.
    /// </summary>
    private sealed class StatsRequest
    {
        [JsonPropertyName("applicationId")]
        public string AppId { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string AppVersion { get; set; } = string.Empty;
    }
}
