using System.Net;
using System.Text.Json;

namespace Ddns.Core;

/// <summary>
/// Resolves the public IP address from a plain-text (icanhazip, ipify, ...) or
/// JSON (<c>{"ip":"..."}</c>) endpoint.
/// </summary>
public sealed class PublicIpService(HttpClient httpClient)
{
    public const string DefaultServiceUrl = "https://api64.ipify.org";

    public async Task<IPAddress> GetPublicAddressAsync(string? serviceUrl, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(serviceUrl) ? DefaultServiceUrl : serviceUrl.Trim();

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"IP service URL is not an absolute URL: '{url}'.");

        var response = await httpClient.GetStringAsync(uri, cancellationToken);
        var candidate = ExtractCandidate(response);

        return IPAddress.TryParse(candidate, out var address)
            ? address
            : throw new InvalidOperationException(
                $"IP service '{uri}' returned an unexpected response: '{Truncate(response)}'.");
    }

    private static string ExtractCandidate(string response)
    {
        var text = response.Trim();
        if (text.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (var propertyName in new[] { "ip", "IPv4", "IPv6", "address" })
                        if (document.RootElement.TryGetProperty(propertyName, out var value)
                            && value.ValueKind == JsonValueKind.String)
                            return value.GetString()!.Trim();
                }
            }
            catch (JsonException)
            {
                // Fall through to the plain-text parsing below.
            }
        }

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .FirstOrDefault() ?? text;
    }

    private static string Truncate(string value)
    {
        return value.Length <= 120 ? value : value[..120] + "...";
    }
}
