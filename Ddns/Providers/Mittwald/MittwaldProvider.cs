using System.Net;
using Ddns.Config;
using Ddns.Core;

namespace Ddns.Providers.Mittwald;

/// <summary>
/// Adapts the mittwald v1 and v2 API clients to the <see cref="IDnsProvider"/>
/// contract. The API version comes from the account override or the global
/// <c>default_api_version</c>.
/// </summary>
public sealed class MittwaldProvider(HttpClient httpClient, int apiVersion) : IDnsProvider
{
    public string Type => ProviderTypes.Mittwald;

    public async Task ValidateAsync(ConfigAccount account, CancellationToken cancellationToken = default)
    {
        EnsureCredentials(account);

        if (ResolveVersion() == 1)
            await new MittwaldV1(httpClient, account.ApiKey)
                .GetDnsOverviewAsync(account.Name, cancellationToken);
        else
            await new MittwaldV2(httpClient, account.ApiKey).ListDomainsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiscoveredTarget>> DiscoverAsync(
        ConfigAccount account,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials(account);

        return ResolveVersion() == 1
            ? await DiscoverV1Async(account, cancellationToken)
            : await DiscoverV2Async(account, cancellationToken);
    }

    public async Task<DnsUpdateResult> UpdateAsync(
        ConfigAccount account,
        ConfigRecord record,
        IPAddress ipAddress,
        CancellationToken cancellationToken = default)
    {
        EnsureCredentials(account);

        if (string.IsNullOrWhiteSpace(record.Domain))
            throw new InvalidOperationException("A mittwald record needs a domain name.");

        return ResolveVersion() == 1
            ? await UpdateV1Async(account, record, ipAddress, cancellationToken)
            : await UpdateV2Async(account, record, ipAddress, cancellationToken);
    }

    private async Task<IReadOnlyList<DiscoveredTarget>> DiscoverV1Async(
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.Name))
            throw new InvalidOperationException("Mittwald API v1 needs the mittwald account name or numeric UID as account name.");

        var domains = await new MittwaldV1(httpClient, account.ApiKey)
            .GetDnsOverviewAsync(account.Name, cancellationToken);

        return domains
            .Select(domain => new DiscoveredTarget(FirstNonEmpty(domain.FullName, domain.DomainName)))
            .Where(target => !string.IsNullOrWhiteSpace(target.Domain))
            .OrderBy(target => target.Domain, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<DiscoveredTarget>> DiscoverV2Async(
        ConfigAccount account,
        CancellationToken cancellationToken)
    {
        var client = new MittwaldV2(httpClient, account.ApiKey);
        var domains = await client.ListDomainsAsync(cancellationToken);
        var zonesByProject = new Dictionary<string, IReadOnlyList<V2DnsZone>>(StringComparer.Ordinal);
        var discovered = new List<DiscoveredTarget>();

        foreach (var domain in domains.Where(domain => domain.Connected))
        {
            IReadOnlyList<V2DnsZone> zones = [];
            var projectId = domain.ProjectId;
            if (!string.IsNullOrWhiteSpace(projectId))
            {
                if (zonesByProject.TryGetValue(projectId, out var cachedZones))
                {
                    zones = cachedZones;
                }
                else
                {
                    zones = await client.ListDnsZonesAsync(projectId, cancellationToken);
                    zonesByProject.Add(projectId, zones);
                }
            }

            var zone = zones.FirstOrDefault(candidate =>
                string.Equals(candidate.Domain, domain.Domain, StringComparison.OrdinalIgnoreCase));

            discovered.Add(new DiscoveredTarget(
                domain.Domain,
                zone?.Id ?? domain.DomainId,
                ProjectId: domain.ProjectId));
        }

        return discovered
            .OrderBy(target => target.Domain, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<DnsUpdateResult> UpdateV1Async(
        ConfigAccount account,
        ConfigRecord record,
        IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account.Name))
            throw new InvalidOperationException("Mittwald API v1 needs the mittwald account name or numeric UID as account name.");

        await new MittwaldV1(httpClient, account.ApiKey)
            .UpdateTargetAsync(account.Name, record.Domain, ipAddress, cancellationToken);

        return DnsUpdateResult.Updated();
    }

    private async Task<DnsUpdateResult> UpdateV2Async(
        ConfigAccount account,
        ConfigRecord record,
        IPAddress ipAddress,
        CancellationToken cancellationToken)
    {
        var client = new MittwaldV2(httpClient, account.ApiKey);
        var dnsZoneId = record.ZoneId;

        if (string.IsNullOrWhiteSpace(dnsZoneId))
        {
            if (string.IsNullOrWhiteSpace(record.ProjectId))
                throw new InvalidOperationException(
                    $"Mittwald record '{record.Domain}' has no zone_id and no project_id to resolve it.");

            var zones = await client.ListDnsZonesAsync(record.ProjectId, cancellationToken);
            dnsZoneId = zones.FirstOrDefault(zone =>
                string.Equals(zone.Domain, record.Domain, StringComparison.OrdinalIgnoreCase))?.Id;

            if (string.IsNullOrWhiteSpace(dnsZoneId))
                throw new InvalidOperationException(
                    $"No mittwald DNS zone found for '{record.Domain}' in project '{record.ProjectId}'.");
        }

        await client.UpdateARecordSetAsync(dnsZoneId, ipAddress, cancellationToken);
        return DnsUpdateResult.Updated();
    }

    private int ResolveVersion()
    {
        return apiVersion switch
        {
            1 => 1,
            2 => 2,
            _ => throw new InvalidOperationException($"Unsupported mittwald API version: {apiVersion}.")
        };
    }

    private static void EnsureCredentials(ConfigAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.ApiKey))
            throw new InvalidOperationException($"Account '{account.Name}' has no api_key.");

        if (string.IsNullOrWhiteSpace(account.Name))
            throw new InvalidOperationException("A mittwald account needs a name.");
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
