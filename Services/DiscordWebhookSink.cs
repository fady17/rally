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
    /// <summary>
    /// A custom Serilog sink that sends log events to a specified Discord webhook.
    /// This is used in the v1 Logistics IdP to provide real-time alerts for critical
    /// security and operational events, such as login failures or administrative actions.
    /// </summary>
    public class DiscordWebhookSink : ILogEventSink, IDisposable
    {
        private readonly string _webhookUrl;
        private readonly LogEventLevel _restrictedToMinimumLevel;
        private readonly IFormatProvider? _formatProvider;
        private readonly HttpClient _httpClient;
        // A semaphore is used to ensure that log events are sent to Discord one at a time,
        // preventing rate-limiting issues and ensuring message order.
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="DiscordWebhookSink"/> class.
        /// </summary>
        /// <param name="webhookUrl">The URL of the Discord webhook to post messages to.</param>
        /// <param name="httpClientFactory">The factory to create HttpClient instances.</param>
        /// <param name="restrictedToMinimumLevel">The minimum log level required for an event to be processed by this sink.</param>
        /// <param name="formatProvider">An optional format provider for rendering log messages.</param>
        public DiscordWebhookSink(
            string webhookUrl,
            IHttpClientFactory httpClientFactory,
            LogEventLevel restrictedToMinimumLevel,
            IFormatProvider? formatProvider = null)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
                throw new ArgumentNullException(nameof(webhookUrl));
            if (httpClientFactory == null)
                throw new ArgumentNullException(nameof(httpClientFactory));

            _webhookUrl = webhookUrl;
            _restrictedToMinimumLevel = restrictedToMinimumLevel;
            _formatProvider = formatProvider;
            // Create a dedicated HttpClient for this sink using the factory.
            _httpClient = httpClientFactory.CreateClient("DiscordWebhookClient");
        }

        /// <summary>
        /// The main method called by Serilog to process a log event.
        /// This implementation queues the event to be sent asynchronously to Discord.
        /// </summary>
        /// <param name="logEvent">The log event to be emitted.</param>
        public async void Emit(LogEvent logEvent) // `async void` is a necessary exception for sink implementations.
        {
            // Ignore events that are below the configured minimum level.
            if (logEvent.Level < _restrictedToMinimumLevel)
            {
                return;
            }

            bool acquired = false;
            try
            {
                // Wait for the semaphore with a timeout to prevent deadlocks.
                acquired = await _semaphore.WaitAsync(TimeSpan.FromSeconds(5));
                if (!acquired) {
                     Console.WriteLine($"[DiscordSink] WARN: Could not acquire semaphore for Discord sink, skipping log event. Sink might be overloaded or deadlocked.");
                     return;
                }

                // If the semaphore is acquired, send the log event.
                await SendToDiscordAsync(logEvent);
            }
            catch (Exception ex)
            {
                 // Log any exceptions from the sink to the console to avoid a recursive logging loop.
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

        /// <summary>
        /// Formats a log event and sends it to the Discord webhook as an HTTP POST request.
        /// </summary>
        /// <param name="logEvent">The log event to send.</param>
        private async Task SendToDiscordAsync(LogEvent logEvent)
        {
            var message = logEvent.RenderMessage(_formatProvider);
            var exceptionText = logEvent.Exception?.ToString();
            var levelEmoji = GetLevelEmoji(logEvent.Level);
            var levelText = logEvent.Level.ToString().ToUpperInvariant();

            // Extract the 'SourceContext' property, which usually contains the name of the class that generated the log.
            string sourceContext = logEvent.Properties.TryGetValue("SourceContext", out var scValue)
                                        ? scValue.ToString().Trim('"')
                                        : "UnknownSource";

            // Build a structured, markdown-formatted message for Discord.
            var contentBuilder = new StringBuilder();
            contentBuilder.AppendLine($"{levelEmoji} **{levelText}**");
            contentBuilder.AppendLine($"> Timestamp: {logEvent.Timestamp:yyyy-MM-dd HH:mm:ss.fff K}");
            contentBuilder.AppendLine($"> Source: `{sourceContext}`");

            // Include any custom enriched properties, like 'AlertType' or 'RequestId'.
             if (logEvent.Properties.TryGetValue("AlertType", out var alertTypeValue)) {
                 contentBuilder.AppendLine($"> AlertType: `{alertTypeValue.ToString().Trim('"')}`");
             }
             if (logEvent.Properties.TryGetValue("RequestId", out var requestIdValue)) {
                 contentBuilder.AppendLine($"> RequestId: `{requestIdValue.ToString().Trim('"')}`");
             }

            contentBuilder.AppendLine($"```{Environment.NewLine}{message}{Environment.NewLine}```");

            if (exceptionText != null)
            {
                contentBuilder.AppendLine($"**Exception:**{Environment.NewLine}```{Environment.NewLine}{exceptionText}{Environment.NewLine}```");
            }

            var fullContent = contentBuilder.ToString();

            // Truncate the message if it exceeds Discord's character limit.
            if (fullContent.Length > 1990)
            {
                fullContent = fullContent.Substring(0, 1990) + "\n... (truncated)";
            }
            
            // Create the JSON payload for the webhook.
            var payload = new { content = fullContent };

            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            using var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            try
            {
                 using var response = await _httpClient.PostAsync(_webhookUrl, httpContent);

                 if (!response.IsSuccessStatusCode)
                 {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[DiscordSink] ERROR: Failed posting to Discord. Status: {response.StatusCode}. Reason: {response.ReasonPhrase}. Response: {responseBody}");
                 }
            }
            catch (HttpRequestException httpEx)
            {
                 Console.WriteLine($"[DiscordSink] ERROR: HTTP request failed sending to Discord: {httpEx.Message}");
            }
            catch (TaskCanceledException taskEx)
            {
                  Console.WriteLine($"[DiscordSink] ERROR: Task cancelled (timeout?) sending to Discord: {taskEx.Message}");
            }
        }

        /// <summary>
        /// A helper method to select an emoji based on the log event's severity level.
        /// </summary>
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
        
        /// <summary>
        /// Implements the standard IDisposable pattern to release managed resources like the SemaphoreSlim.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _semaphore?.Dispose();
                }
                _disposed = true;
            }
        }
        
        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Provides a user-friendly extension method to register the <see cref="DiscordWebhookSink"/>
    /// in the Serilog logger configuration pipeline.
    /// </summary>
    public static class DiscordWebhookSinkExtensions
    {
        /// <summary>
        /// Adds the DiscordWebhook sink to the logger configuration.
        /// </summary>
        public static Serilog.LoggerConfiguration DiscordWebhook(
                  this LoggerSinkConfiguration sinkConfiguration,
                  string webhookUrl,
                  IHttpClientFactory httpClientFactory,
                  LogEventLevel restrictedToMinimumLevel = LogEventLevel.Information,
                  IFormatProvider? formatProvider = null)
        {
            if (sinkConfiguration == null) throw new ArgumentNullException(nameof(sinkConfiguration));
            if (httpClientFactory == null) throw new ArgumentNullException(nameof(httpClientFactory));
            if (string.IsNullOrWhiteSpace(webhookUrl)) throw new ArgumentNullException(nameof(webhookUrl));

            ILogEventSink sink = new DiscordWebhookSink(webhookUrl, httpClientFactory, restrictedToMinimumLevel, formatProvider);

            return sinkConfiguration.Sink(sink, restrictedToMinimumLevel);
        }
    }
}