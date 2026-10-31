using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace Ddns.Notifications;

/// <summary>
/// Best-effort webhook notifications. The payload is <c>{"content":"..."}</c>,
/// which works with Discord webhooks and is accepted by most chat webhooks.
/// </summary>
public sealed class WebhookNotifier(HttpClient httpClient, ILogger<WebhookNotifier> logger)
{
    public async Task SendAsync(string? webhookUrl, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl)) return;

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
        {
            logger.LogWarning("Ignoring webhook with invalid URL: {WebhookUrl}", webhookUrl);
            return;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                uri, new { content = message }, cancellationToken);

            if (!response.IsSuccessStatusCode)
                logger.LogWarning(
                    "Webhook {WebhookUrl} returned HTTP {StatusCode}",
                    uri,
                    (int)response.StatusCode);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Webhook delivery to {WebhookUrl} failed", uri);
        }
    }
}
