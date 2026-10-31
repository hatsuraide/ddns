using Ddns.Config;
using Ddns.Core;

namespace Ddns.Tests.Config;

public sealed class ConfigValidatorTests
{
    [Fact]
    public void ValidConfig_HasNoErrors()
    {
        Assert.Empty(ConfigValidator.Validate(CreateValidConfig()));
    }

    [Fact]
    public void MissingApiKey_IsReported()
    {
        var config = CreateValidConfig();
        config.Accounts[0].ApiKey = string.Empty;

        Assert.Contains(ConfigValidator.Validate(config), error => error.Contains("api_key"));
    }

    [Fact]
    public void UnsupportedProviderType_IsReported()
    {
        var config = CreateValidConfig();
        config.Accounts[0].Type = "route53";

        Assert.Contains(ConfigValidator.Validate(config), error => error.Contains("unsupported type"));
    }

    [Fact]
    public void DuplicateAccountNames_AreReported()
    {
        var config = CreateValidConfig();
        config.Accounts.Add(new ConfigAccount
        {
            Type = ProviderTypes.Mittwald,
            Name = "cf-main",
            ApiKey = "key",
            Records = [new ConfigRecord { Domain = "example.com" }]
        });

        Assert.Contains(ConfigValidator.Validate(config), error => error.Contains("must be unique"));
    }

    [Fact]
    public void InvalidIpServiceUrl_IsReported()
    {
        var config = CreateValidConfig();
        config.IpServiceUrl = "not-a-url";

        Assert.Contains(ConfigValidator.Validate(config), error => error.Contains("ip_service_url"));
    }

    [Fact]
    public void RecordWithoutDomain_IsReported()
    {
        var config = CreateValidConfig();
        config.Accounts[0].Records[0].Domain = string.Empty;

        Assert.Contains(ConfigValidator.Validate(config), error => error.Contains("needs a domain"));
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
                            ZoneId = "zone-1",
                            RecordId = "rec-1"
                        }
                    ]
                }
            ]
        };
    }
}
