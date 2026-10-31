using Ddns.Core;

namespace Ddns.Config;

public static class ConfigValidator
{
    public static IReadOnlyList<string> Validate(ConfigModel config)
    {
        var errors = new List<string>();

        if (config.Timeout <= TimeSpan.Zero) errors.Add("timeout must be greater than zero.");

        if (!IsSupportedApiVersion(config.DefaultApiVersion)) errors.Add("default_api_version must be 1 or 2.");

        if (!IsValidOptionalUrl(config.GlobalWebhook)) errors.Add("global_webhook must be an absolute URL when set.");

        if (!IsValidRequiredUrl(config.IpServiceUrl)) errors.Add("ip_service_url must be an absolute URL.");

        if (config.Accounts.Count == 0) errors.Add("at least one account is required.");

        var accountNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var accountIndex = 0; accountIndex < config.Accounts.Count; accountIndex++)
        {
            var account = config.Accounts[accountIndex];
            var accountLabel = string.IsNullOrWhiteSpace(account.Name)
                ? $"accounts[{accountIndex}]"
                : $"account '{account.Name}'";

            if (string.IsNullOrWhiteSpace(account.Name))
                errors.Add($"accounts[{accountIndex}].name is required.");
            else if (!accountNames.Add(account.Name)) errors.Add($"account name '{account.Name}' must be unique.");

            if (!ProviderTypes.IsSupported(account.Type))
                errors.Add($"{accountLabel} has unsupported type '{account.Type}'.");

            if (string.IsNullOrWhiteSpace(account.ApiKey)) errors.Add($"{accountLabel} needs an api_key.");

            if (account.ApiVersion is not null && !IsSupportedApiVersion(account.ApiVersion.Value))
                errors.Add($"{accountLabel} api_version must be 1 or 2 when set.");

            if (!IsValidOptionalUrl(account.Webhook))
                errors.Add($"{accountLabel} webhook must be an absolute URL when set.");

            for (var recordIndex = 0; recordIndex < account.Records.Count; recordIndex++)
            {
                var record = account.Records[recordIndex];
                var recordLabel = string.IsNullOrWhiteSpace(record.Domain)
                    ? $"{accountLabel} records[{recordIndex}]"
                    : $"{accountLabel} record '{record.Domain}'";

                if (string.IsNullOrWhiteSpace(record.Domain))
                    errors.Add($"{recordLabel} needs a domain.");

                if (!IsValidOptionalUrl(record.Webhook))
                    errors.Add($"{recordLabel} webhook must be an absolute URL when set.");

                if (record.Ttl is < 1)
                    errors.Add($"{recordLabel} ttl must be a positive number.");
            }
        }

        return errors;
    }

    public static void ThrowIfInvalid(ConfigModel config)
    {
        var errors = Validate(config);
        if (errors.Count > 0) throw new InvalidOperationException("Config is invalid: " + string.Join(" ", errors));
    }

    public static bool IsSupportedApiVersion(int apiVersion)
    {
        return apiVersion is 1 or 2;
    }

    private static bool IsValidOptionalUrl(string? url)
    {
        return string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    private static bool IsValidRequiredUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out _);
    }
}
