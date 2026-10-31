using Ddns.Config;

namespace Ddns.Tests.Config;

[Collection(EnvironmentCollection.Name)]
public sealed class ConfigEnvironmentTests
{
    [Fact]
    public void MitwaldEnvironmentVariables_AreIgnored()
    {
        var previous = Snapshot();

        try
        {
            Environment.SetEnvironmentVariable("DDNS_CONFIG_PATH", null);
            Environment.SetEnvironmentVariable("DDNS_SECRET", null);
            Environment.SetEnvironmentVariable("DDNS_IP_SERVICE_URL", null);
            Environment.SetEnvironmentVariable("MITTWALD_CONFIG_PATH", "/tmp/legacy.json");
            Environment.SetEnvironmentVariable("MITTWALD_SECRET", "legacy-secret");
            Environment.SetEnvironmentVariable("MITTWALD_IP_SERVICE_URL", "https://legacy.example/ip");

            Assert.Equal(ConfigStore.DefaultConfigPath, ConfigEnvironment.ConfigPath);
            Assert.Null(ConfigEnvironment.Secret);
            Assert.Null(ConfigEnvironment.IpServiceUrl);
        }
        finally
        {
            Restore(previous);
        }
    }

    [Fact]
    public void DdnsEnvironmentVariables_AreUsed()
    {
        var previous = Snapshot();

        try
        {
            Environment.SetEnvironmentVariable("DDNS_CONFIG_PATH", "/tmp/ddns.json");
            Environment.SetEnvironmentVariable("DDNS_SECRET", "secret");
            Environment.SetEnvironmentVariable("DDNS_IP_SERVICE_URL", "https://example.com/ip");

            Assert.Equal("/tmp/ddns.json", ConfigEnvironment.ConfigPath);
            Assert.Equal("secret", ConfigEnvironment.Secret);
            Assert.Equal("https://example.com/ip", ConfigEnvironment.IpServiceUrl);
        }
        finally
        {
            Restore(previous);
        }
    }

    private static string?[] Snapshot()
    {
        return
        [
            Environment.GetEnvironmentVariable("DDNS_CONFIG_PATH"),
            Environment.GetEnvironmentVariable("DDNS_SECRET"),
            Environment.GetEnvironmentVariable("DDNS_IP_SERVICE_URL"),
            Environment.GetEnvironmentVariable("MITTWALD_CONFIG_PATH"),
            Environment.GetEnvironmentVariable("MITTWALD_SECRET"),
            Environment.GetEnvironmentVariable("MITTWALD_IP_SERVICE_URL")
        ];
    }

    private static void Restore(string?[] values)
    {
        var names = new[]
        {
            "DDNS_CONFIG_PATH", "DDNS_SECRET", "DDNS_IP_SERVICE_URL",
            "MITTWALD_CONFIG_PATH", "MITTWALD_SECRET", "MITTWALD_IP_SERVICE_URL"
        };

        for (var index = 0; index < names.Length; index++)
            Environment.SetEnvironmentVariable(names[index], values[index]);
    }
}
