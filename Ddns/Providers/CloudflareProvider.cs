using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ddns.Config;
using Ddns.Core;

namespace Ddns.Providers;

/// <summary>
/// Cloudflare DNS provider. Uses an API token with DNS read/write and zone
/// read permissions. Records can be addressed by <c>record_id</c> or resolved
/// by <c>domain</c>; the zone is taken from <c>zone_id</c> or resolved as the
/// longest matching zone name.
/// </summary>
public sealed class CloudflareProvider(HttpClient httpClient) : IDnsProvider
{
    private const int MaxPages = 100;
    private static readonly Uri BaseUri = new("https://api.cloudflare.com/client/v4/");
    private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;

    public string Type => ProviderTypes.Cloudflare;

    public async Task ValidateAsync(ConfigAccount account, CancellationToken cancellationToken = default)
    {
        EnsureToken(account);
        await SendEnvelopeAsync<List<CloudflareZone>>(
            HttpMethod.Get, "zones?per_page=1&page=1", account.ApiKey, null, cancellationToken);
    }

    public async Task<IReadOnlyList<DiscoveredTarget>> DiscoverAsync(
        ConfigAccount account,
        CancellationToken cancellationToken = default)
    {
        EnsureToken(account);

        var discovered = new List<DiscoveredTarget>();
        foreach (var zone in await ListAllZonesAsync(account.ApiKey, cancellationToken))
            foreach (var record in await ListAllDnsRecordsAsync(account.ApiKey, zone.Id, cancellationToken))
                if (IsAddressRecord(record.Type))
                    discovered.Add(new DiscoveredTarget(record.Name, zone.Id, record.Id));

        return discovered
            .OrderBy(target => target.Domain, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<DnsUpdateResult> UpdateAsync(
        ConfigAccount account,
        ConfigRecord record,
        IPAddress ipAddress,
        CancellationToken cancellationToken = default)
    {
        EnsureToken(account);

        if (string.IsNullOrWhiteSpace(record.Domain))
            throw new InvalidOperationException("A Cloudflare record needs a domain name.");

        var zoneId = string.IsNullOrWhiteSpace(record.ZoneId)
            ? await ResolveZoneIdAsync(account.ApiKey, record.Domain, cancellationToken)
            : record.ZoneId;

        var recordType = ipAddress.AddressFamily == AddressFamily.InterNetworkV6 ? "AAAA" : "A";
        var existing = string.IsNullOrWhiteSpace(record.RecordId)
            ? await FindRecordAsync(account.ApiKey, zoneId, record.Domain, recordType, cancellationToken)
            : await GetRecordAsync(account.ApiKey, zoneId, record.RecordId, cancellationToken);

        if (existing is null)
            throw new InvalidOperationException(
                $"No {recordType} record named '{record.Domain}' exists in Cloudflare zone '{zoneId}'. " +
                "Create the record in Cloudflare or configure its record_id.");

        if (!string.Equals(existing.Type, recordType, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Cloudflare record '{record.Domain}' is {existing.Type}, but the public IP is {recordType}. " +
                "Point the IP service at the matching address family.");

        var content = ipAddress.ToString();
        if (string.Equals(existing.Content, content, StringComparison.Ordinal))
            return DnsUpdateResult.Unchanged(existing.Content);

        var request = new CloudflareUpdateRecordRequest(
            existing.Type,
            record.Domain,
            content,
            record.Ttl ?? existing.Ttl,
            record.Proxied ?? existing.Proxied);

        await SendEnvelopeAsync<CloudflareDnsRecord>(
            HttpMethod.Put,
            $"zones/{Uri.EscapeDataString(zoneId)}/dns_records/{Uri.EscapeDataString(existing.Id)}",
            account.ApiKey,
            request,
            cancellationToken);

        return DnsUpdateResult.Updated(existing.Content);
    }

    private async Task<CloudflareDnsRecord> GetRecordAsync(
        string token,
        string zoneId,
        string recordId,
        CancellationToken cancellationToken)
    {
        var envelope = await SendEnvelopeAsync<CloudflareDnsRecord>(
            HttpMethod.Get,
            $"zones/{Uri.EscapeDataString(zoneId)}/dns_records/{Uri.EscapeDataString(recordId)}",
            token,
            null,
            cancellationToken);

        return envelope.Result
               ?? throw new InvalidOperationException($"Cloudflare record '{recordId}' was not found.");
    }

    private async Task<CloudflareDnsRecord?> FindRecordAsync(
        string token,
        string zoneId,
        string domain,
        string recordType,
        CancellationToken cancellationToken)
    {
        var records = await ListAllDnsRecordsAsync(token, zoneId, cancellationToken);

        return records.FirstOrDefault(record =>
                   string.Equals(record.Name, domain, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(record.Type, recordType, StringComparison.OrdinalIgnoreCase))
               ?? records.FirstOrDefault(record =>
                   string.Equals(record.Name, domain, StringComparison.OrdinalIgnoreCase)
                   && IsAddressRecord(record.Type));
    }

    private async Task<string> ResolveZoneIdAsync(string token, string domain, CancellationToken cancellationToken)
    {
        var zones = await ListAllZonesAsync(token, cancellationToken);
        var zone = zones
            .Where(candidate => IsWithinZone(domain, candidate.Name))
            .OrderByDescending(candidate => candidate.Name.Length)
            .FirstOrDefault();

        return zone?.Id
               ?? throw new InvalidOperationException(
                   $"No Cloudflare zone contains '{domain}'. Configure zone_id for this record.");
    }

    private async Task<List<CloudflareZone>> ListAllZonesAsync(string token, CancellationToken cancellationToken)
    {
        var zones = new List<CloudflareZone>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var envelope = await SendEnvelopeAsync<List<CloudflareZone>>(
                HttpMethod.Get, $"zones?per_page=50&page={page}", token, null, cancellationToken);

            zones.AddRange(envelope.Result ?? []);
            if (envelope.ResultInfo is null || page >= envelope.ResultInfo.TotalPages) break;
        }

        return zones;
    }

    private async Task<List<CloudflareDnsRecord>> ListAllDnsRecordsAsync(
        string token,
        string zoneId,
        CancellationToken cancellationToken)
    {
        var records = new List<CloudflareDnsRecord>();

        for (var page = 1; page <= MaxPages; page++)
        {
            var envelope = await SendEnvelopeAsync<List<CloudflareDnsRecord>>(
                HttpMethod.Get,
                $"zones/{Uri.EscapeDataString(zoneId)}/dns_records?per_page=100&page={page}",
                token,
                null,
                cancellationToken);

            records.AddRange(envelope.Result ?? []);
            if (envelope.ResultInfo is null || page >= envelope.ResultInfo.TotalPages) break;
        }

        return records;
    }

    private async Task<CloudflareEnvelope<T>> SendEnvelopeAsync<T>(
        HttpMethod method,
        string relativeUrl,
        string token,
        object? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(BaseUri, relativeUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is not null)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), JsonOptions);
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Cloudflare API returned {(int)response.StatusCode} {response.ReasonPhrase}: {Encoding.UTF8.GetString(payload)}",
                null,
                response.StatusCode);

        var envelope = JsonSerializer.Deserialize<CloudflareEnvelope<T>>(payload, JsonOptions)
                       ?? throw new InvalidOperationException("Cloudflare returned an empty response.");

        if (!envelope.Success)
        {
            var message = envelope.Errors is { Count: > 0 }
                ? string.Join("; ", envelope.Errors.Select(error => error.Message))
                : "unknown error";
            throw new HttpRequestException($"Cloudflare API request failed: {message}");
        }

        return envelope;
    }

    private static bool IsAddressRecord(string type)
    {
        return type is "A" or "AAAA";
    }

    private static bool IsWithinZone(string domain, string zoneName)
    {
        return string.Equals(domain, zoneName, StringComparison.OrdinalIgnoreCase)
               || domain.EndsWith("." + zoneName, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureToken(ConfigAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.ApiKey))
            throw new InvalidOperationException(
                $"Cloudflare account '{account.Name}' has no api_key (API token).");
    }

    private sealed record CloudflareEnvelope<T>(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("errors")] IReadOnlyList<CloudflareError>? Errors,
        [property: JsonPropertyName("result")] T? Result,
        [property: JsonPropertyName("result_info")] CloudflareResultInfo? ResultInfo);

    private sealed record CloudflareError(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string Message);

    private sealed record CloudflareResultInfo(
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("per_page")] int PerPage,
        [property: JsonPropertyName("count")] int Count,
        [property: JsonPropertyName("total_count")] int TotalCount,
        [property: JsonPropertyName("total_pages")] int TotalPages);

    private sealed record CloudflareZone(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record CloudflareDnsRecord(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("ttl")] int Ttl,
        [property: JsonPropertyName("proxied")] bool Proxied);

    private sealed record CloudflareUpdateRecordRequest(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("ttl")] int Ttl,
        [property: JsonPropertyName("proxied")] bool Proxied);
}
