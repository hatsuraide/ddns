using System.Text.Json;
using Ddns.Config;
using Ddns.Core;

namespace Ddns.Tests.Config;

public sealed class ConfigStoreTests
{
    [Fact]
    public void PlainConfig_RoundTripsWithProviderAgnosticJsonShape()
    {
        var path = NewTempPath();
        var config = CreateValidConfig();

        try
        {
            new ConfigStore(path, null).SavePlain(config);

            var json = File.ReadAllText(path);
            Assert.Contains("\"global_webhook\"", json);
            Assert.Contains("\"default_api_version\"", json);
            Assert.Contains("\"ip_service_url\"", json);
            Assert.Contains("\"type\": \"cloudflare\"", json);
            Assert.Contains("\"records\"", json);
            Assert.Contains("\"record_id\"", json);
            Assert.Contains("\"zone_id\"", json);

            var loaded = ConfigStore.LoadFromFile(path, null);
            Assert.Equal(ConfigFileFormat.Plain, loaded.Format);
            Assert.Empty(ConfigValidator.Validate(loaded.Config));
            Assert.Equal("home.example.com", loaded.Config.Accounts[0].Records[0].Domain);
            Assert.Equal(ProviderTypes.Cloudflare, loaded.Config.Accounts[0].Type);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EncryptedConfig_RoundTripsThroughExistingEnvelope()
    {
        var path = NewTempPath();
        var config = CreateValidConfig();

        try
        {
            new ConfigStore(path, "secret").SaveEncrypted(config);

            var json = File.ReadAllText(path);
            Assert.Contains("\"format\"", json);
            Assert.Contains(ConfigStore.EncryptedFormat, json);
            Assert.DoesNotContain("cf-token", json);

            var loaded = ConfigStore.LoadFromFile(path, "secret");
            Assert.Equal(ConfigFileFormat.Encrypted, loaded.Format);
            Assert.Empty(ConfigValidator.Validate(loaded.Config));
            Assert.Equal("cf-main", loaded.Config.Accounts[0].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyFlatMittwaldConfig_StillImports()
    {
        var path = NewTempPath();
        var legacy = new
        {
            ApiKey = "api-key",
            Timeout = TimeSpan.FromMinutes(10),
            GlobalWebhook = "https://example.com/hook",
            GlobalApiVersion = 1,
            Domains = new[]
            {
                new
                {
                    Id = "domain-id",
                    Domain = "example.com",
                    ProjectId = "project-id"
                }
            }
        };

        try
        {
            ConfigStore.WriteAllTextAtomic(path, JsonSerializer.Serialize(legacy, ConfigStore.JsonOptions));

            var loaded = ConfigStore.LoadFromFile(path, null);

            Assert.Equal(ConfigFileFormat.Plain, loaded.Format);
            Assert.Equal("main", loaded.Config.Accounts[0].Name);
            Assert.Equal(ProviderTypes.Mittwald, loaded.Config.Accounts[0].Type);
            Assert.Equal("api-key", loaded.Config.Accounts[0].ApiKey);
            Assert.Equal(1, loaded.Config.Accounts[0].ApiVersion);
            Assert.Equal(TimeSpan.FromMinutes(10), loaded.Config.Timeout);
            Assert.Equal("example.com", loaded.Config.Accounts[0].Records[0].Domain);
            Assert.Equal("domain-id", loaded.Config.Accounts[0].Records[0].ZoneId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyMittwaldAccountConfig_MapsDomainsToRecords()
    {
        var path = NewTempPath();
        const string json = """
                            {
                              "global_webhook": "https://example.com/hook",
                              "default_api_version": 1,
                              "timeout": "00:05:00",
                              "accounts": [
                                {
                                  "name": "r1234",
                                  "api_key": "api-key",
                                  "domains": [
                                    { "id": "zone-id", "domain": "example.com", "project_id": "project-id" }
                                  ]
                                }
                              ]
                            }
                            """;

        try
        {
            ConfigStore.WriteAllTextAtomic(path, json);

            var loaded = ConfigStore.LoadFromFile(path, null);
            var account = loaded.Config.Accounts[0];

            Assert.Equal(ProviderTypes.Mittwald, account.Type);
            Assert.Equal("r1234", account.Name);
            Assert.Equal("api-key", account.ApiKey);
            Assert.Equal("example.com", account.Records[0].Domain);
            Assert.Equal("zone-id", account.Records[0].ZoneId);
            Assert.Equal("project-id", account.Records[0].ProjectId);
            Assert.Empty(ConfigValidator.Validate(loaded.Config));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LegacyDyndnsPyCloudflareConfig_ImportsZonesAsRecords()
    {
        var path = NewTempPath();
        const string json = """
                            {
                              "global_webhook": "https://discord.com/api/webhooks/1/token",
                              "accounts": [
                                {
                                  "api_token": "cf-token",
                                  "zones": [
                                    {
                                      "zone_id": "zone-1",
                                      "zone_name": "example.com",
                                      "records": [
                                        {
                                          "record_id": "record-1",
                                          "domain": "home.example.com",
                                          "webhook": null
                                        }
                                      ]
                                    }
                                  ]
                                }
                              ]
                            }
                            """;

        try
        {
            ConfigStore.WriteAllTextAtomic(path, json);

            var loaded = ConfigStore.LoadFromFile(path, null);
            var account = loaded.Config.Accounts[0];

            Assert.Equal(ProviderTypes.Cloudflare, account.Type);
            Assert.Equal("cf-token", account.ApiKey);
            Assert.Single(account.Records);
            Assert.Equal("home.example.com", account.Records[0].Domain);
            Assert.Equal("zone-1", account.Records[0].ZoneId);
            Assert.Equal("record-1", account.Records[0].RecordId);
            Assert.Null(account.Records[0].Webhook);
            Assert.Empty(ConfigValidator.Validate(loaded.Config));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ConfigModel CreateValidConfig()
    {
        return new ConfigModel
        {
            GlobalWebhook = "https://example.com/hook",
            DefaultApiVersion = 2,
            Timeout = TimeSpan.FromMinutes(5),
            IpServiceUrl = "https://api64.ipify.org",
            Accounts =
            [
                new ConfigAccount
                {
                    Type = ProviderTypes.Cloudflare,
                    Name = "cf-main",
                    ApiKey = "cf-token",
                    Records =
                    [
                        new ConfigRecord
                        {
                            Domain = "home.example.com",
                            ZoneId = "zone-id",
                            RecordId = "record-id"
                        }
                    ]
                }
            ]
        };
    }

    private static string NewTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"ddns-tests-{Guid.NewGuid():N}.json");
    }
}
