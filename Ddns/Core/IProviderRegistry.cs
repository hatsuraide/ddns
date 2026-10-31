using Ddns.Config;

namespace Ddns.Core;

public interface IProviderRegistry
{
    IReadOnlyList<string> SupportedTypes { get; }

    IDnsProvider Create(ConfigAccount account, int defaultApiVersion);
}
