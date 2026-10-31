namespace Ddns.Config;

/// <summary>
/// Resolves configuration locations from environment variables.
/// </summary>
public static class ConfigEnvironment
{
    public static string ConfigPath =>
        Get("DDNS_CONFIG_PATH") ?? ConfigStore.DefaultConfigPath;

    public static string? Secret => Get("DDNS_SECRET");

    public static string? IpServiceUrl => Get("DDNS_IP_SERVICE_URL");

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
