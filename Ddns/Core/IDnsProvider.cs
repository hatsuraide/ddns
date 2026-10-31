using System.Net;
using Ddns.Config;

namespace Ddns.Core;

/// <summary>
/// A DNS provider (mittwald, Cloudflare, ...). Implementations are stateless
/// apart from their shared <see cref="HttpClient"/>, so they are cheap to create.
/// </summary>
public interface IDnsProvider
{
    /// <summary>Provider identifier from <see cref="ProviderTypes"/>.</summary>
    string Type { get; }

    /// <summary>Checks that the configured credentials are usable. Throws on failure.</summary>
    Task ValidateAsync(ConfigAccount account, CancellationToken cancellationToken = default);

    /// <summary>Lists records that can be managed with the configured credentials.</summary>
    Task<IReadOnlyList<DiscoveredTarget>> DiscoverAsync(
        ConfigAccount account,
        CancellationToken cancellationToken = default);

    /// <summary>Points the given record at <paramref name="ipAddress"/>.</summary>
    Task<DnsUpdateResult> UpdateAsync(
        ConfigAccount account,
        ConfigRecord record,
        IPAddress ipAddress,
        CancellationToken cancellationToken = default);
}
