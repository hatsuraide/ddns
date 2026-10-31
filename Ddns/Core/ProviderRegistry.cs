using Ddns.Config;
using Ddns.Providers;
using Ddns.Providers.Mittwald;

namespace Ddns.Core;

/// <summary>
/// Maps a configured <c>type</c> to a provider implementation. Adding a new
/// provider only requires a new <see cref="IDnsProvider"/> and an entry here.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, Func<ConfigAccount, int, IDnsProvider>> _factories;

    public ProviderRegistry(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);

        _httpClient = httpClient;
        _factories = new Dictionary<string, Func<ConfigAccount, int, IDnsProvider>>(StringComparer.OrdinalIgnoreCase)
        {
            [ProviderTypes.Mittwald] = (account, defaultApiVersion) =>
                new MittwaldProvider(_httpClient, account.ApiVersion ?? defaultApiVersion),
            [ProviderTypes.Cloudflare] = (_, _) => new CloudflareProvider(_httpClient)
        };
    }

    public IReadOnlyList<string> SupportedTypes => ProviderTypes.All;

    public IDnsProvider Create(ConfigAccount account, int defaultApiVersion)
    {
        ArgumentNullException.ThrowIfNull(account);

        var type = ProviderTypes.Normalize(account.Type);
        return _factories.TryGetValue(type, out var factory)
            ? factory(account, defaultApiVersion)
            : throw new NotSupportedException(
                $"Unsupported provider type '{account.Type}'. Supported types: {string.Join(", ", SupportedTypes)}.");
    }
}
