namespace Ddns.Config;

/// <summary>
/// Resolves configuration locations from environment variables. The mittwald
/// names are still honoured so existing deployments keep working.
/// </summary>
public static class ConfigEnvironment
{
    public static string ConfigPath =>
        Get("DDNS_CONFIG_PATH") ?? Get("MITTWALD_CONFIG_PATH") ?? ConfigStore.DefaultConfigPath;

    public static string? Secret => Get("DDNS_SECRET") ?? Get("MITTWALD_SECRET");

    public static string? IpServiceUrl => Get("DDNS_IP_SERVICE_URL") ?? Get("MITTWALD_IP_SERVICE_URL");

    public static ConfigStore CreateStore()
    {
        return new ConfigStore(ConfigPath, Secret);
    }

    private static string? Get(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
