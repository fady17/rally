// File: Rally/Services/DiscordWebhookSink.cs
#nullable enable 

using Serilog.Core;
using Serilog.Events;
using System.Net.Http;
using System.Net.Http.Json; // For cleaner JSON posting (optional)
using System.Text;
using System.Text.Json;
using System.Threading; // Required for SemaphoreSlim
using System.Threading.Tasks;
using System;
using Serilog.Configuration;

namespace Rally.Services 
{
    public class DiscordWebhookSink : ILogEventSink, IDisposable
    {
        private readonly string _webhookUrl;
        private readonly LogEventLevel _restrictedToMinimumLevel;
        private readonly IFormatProvider? _formatProvider; // Nullable IFormatProvider
        private readonly HttpClient _httpClient;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1); // Control concurrency
        private bool _disposed = false; // To detect redundant calls

        public DiscordWebhookSink(
            string webhookUrl,
            IHttpClientFactory httpClientFactory,
            LogEventLevel restrictedToMinimumLevel,
            IFormatProvider? formatProvider = null) // Nullable parameter with null default
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
                throw new ArgumentNullException(nameof(webhookUrl));
            if (httpClientFactory == null)
                throw new ArgumentNullException(nameof(httpClientFactory));

            _webhookUrl = webhookUrl;
            _restrictedToMinimumLevel = restrictedToMinimumLevel;
            _formatProvider = formatProvider; // Assign nullable
            _httpClient = httpClientFactory.CreateClient("DiscordWebhookClient");
        }

        public async void Emit(LogEvent logEvent) // async void is generally discouraged, but common in sinks
        {
            if (logEvent.Level < _restrictedToMinimumLevel)
            {
                return;
            }

            // Use a timeout for waiting to prevent deadlocks if something goes wrong
            bool acquired = false;
            try
            {
                acquired = await _semaphore.WaitAsync(TimeSpan.FromSeconds(5)); // Wait max 5 seconds
                if (!acquired) {
                     Console.WriteLine($"[DiscordSink] WARN: Could not acquire semaphore for Discord sink, skipping log event. Sink might be overloaded or deadlocked.");
                     return;
                }

                await SendToDiscordAsync(logEvent);
            }
            catch (Exception ex)
            {
                 // Log sink exception to console to avoid loop
                Console.WriteLine($"[DiscordSink] ERROR: Exception sending log event to Discord: {ex}");
            }
            finally
            {
                 if (acquired)
                 {
                     _semaphore.Release();
                 }
            }
        }

        private async Task SendToDiscordAsync(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage(_formatProvider);
            var exceptionText = logEvent.Exception?.ToString();
            var levelEmoji = GetLevelEmoji(logEvent.Level);
            var levelText = logEvent.Level.ToString().ToUpperInvariant();

            // Try to get SourceContext for more info
            string sourceContext = logEvent.Properties.TryGetValue("SourceContext", out var scValue)
                                        ? scValue.ToString().Trim('"') // Remove quotes Serilog might add
                                        : "UnknownSource";

            // Create a more structured message potentially
            var contentBuilder = new StringBuilder();
            contentBuilder.AppendLine($"{levelEmoji} **{levelText}**");
            contentBuilder.AppendLine($"> Timestamp: {logEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff K}");
            contentBuilder.AppendLine($"> Source: `{sourceContext}`");

            // Add enriched properties if they exist (like AlertType)
             if (logEvent.Properties.TryGetValue("AlertType", out var alertTypeValue)) {
                 contentBuilder.AppendLine($"> AlertType: `{alertTypeValue.ToString().Trim('"')}`");
             }
             if (logEvent.Properties.TryGetValue("RequestId", out var requestIdValue)) {
                 contentBuilder.AppendLine($"> RequestId: `{requestIdValue.ToString().Trim('"')}`");
             }
             // Add other relevant properties here...

            contentBuilder.AppendLine($"```{Environment.NewLine}{message}{Environment.NewLine}```"); // Message in code block

            if (exceptionText != null)
            {
                contentBuilder.AppendLine($"**Exception:**{Environment.NewLine}```{Environment.NewLine}{exceptionText}{Environment.NewLine}```");
            }

            var fullContent = contentBuilder.ToString();

            // Discord message limit is 2000 chars
            if (fullContent.Length > 1990)
            {
                fullContent = fullContent.Substring(0, 1990) + "\n... (truncated)";
            }

            var payload = new { content = fullContent }; // Basic payload

            // Consider using Discord Embeds for richer formatting:
            /*
            var payload = new
            {
                embeds = new[] {
                    new {
                        title = $"{levelEmoji} {levelText}: {sourceContext}",
                        description = message,
                        color = GetLevelColor(logEvent.Level), // e.g., 15158332 for Error (Red)
                        fields = exceptionText != null ? new[] { new { name = "Exception", value = $"```{Truncate(exceptionText, 1000)}```" } } : null,
                        timestamp = logEvent.Timestamp.ToString("o") // ISO 8601 format
                    }
                }
            };
            */

            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                 using var response = await _httpClient.PostAsync(_webhookUrl, httpContent);

                 if (!response.IsSuccessStatusCode)
                 {
                    // Log failure to console
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[DiscordSink] ERROR: Failed posting to Discord. Status: {response.StatusCode}. Reason: {response.ReasonPhrase}. Response: {responseBody}");
                 }
            }
            catch (HttpRequestException httpEx)
            {
                 Console.WriteLine($"[DiscordSink] ERROR: HTTP request failed sending to Discord: {httpEx.Message}");
            }
            catch (TaskCanceledException taskEx) // Handles timeouts if HttpClient is configured with one
            {
                  Console.WriteLine($"[DiscordSink] ERROR: Task cancelled (timeout?) sending to Discord: {taskEx.Message}");
            }
        }

        private string GetLevelEmoji(LogEventLevel level) => level switch
        {
            LogEventLevel.Fatal => "🚨",
            LogEventLevel.Error => "❌",
            LogEventLevel.Warning => "⚠️",
            LogEventLevel.Information => "ℹ️",
            LogEventLevel.Debug => "⚙️",
            LogEventLevel.Verbose => "📝",
            _ => "❓"
        };

        // Optional: Helper for Embed color
        // private int GetLevelColor(LogEventLevel level) => level switch
        // {
        //     LogEventLevel.Fatal => 15158332, // Dark Red
        //     LogEventLevel.Error => 15158332, // Dark Red
        //     LogEventLevel.Warning => 16776960, // Yellow
        //     LogEventLevel.Information => 3447003, // Blue
        //     LogEventLevel.Debug => 10070709, // Purple
        //     LogEventLevel.Verbose => 12370112, // Grey
        //     _ => 0
        // };

        // Optional: Helper to truncate long strings for embeds
        // private string Truncate(string value, int maxLength) =>
        //     value.Length <= maxLength ? value : value.Substring(0, maxLength - 3) + "...";


        // Implement IDisposable pattern
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                    _semaphore?.Dispose();
                    // HttpClient from factory is managed by the factory, typically no need to dispose here.
                }

                // Free unmanaged resources (unmanaged objects) and override finalizer
                // Set large fields to null
                _disposed = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    // Extension method for easier configuration
    public static class DiscordWebhookSinkExtensions
    {
        public static Serilog.LoggerConfiguration DiscordWebhook(
                  this LoggerSinkConfiguration sinkConfiguration,
                  string webhookUrl,
                  IHttpClientFactory httpClientFactory,
                  LogEventLevel restrictedToMinimumLevel = LogEventLevel.Information, // Default to Info for alerts
                  IFormatProvider? formatProvider = null) // Nullable parameter
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));
            if (httpClientFactory == null) throw new ArgumentNullException(nameof(httpClientFactory));
            if (string.IsNullOrWhiteSpace(webhookUrl)) throw new ArgumentNullException(nameof(webhookUrl));


            ILogEventSink sink = new DiscordWebhookSink(webhookUrl, httpClientFactory, restrictedToMinimumLevel, formatProvider);

            // Apply the restriction using the level passed to the sink, not the extension method default
            return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
        }
    }
}