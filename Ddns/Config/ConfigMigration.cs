using System.Text.Json;
using System.Text.Json.Nodes;
using Ddns.Core;

namespace Ddns.Config;

/// <summary>
/// Converts legacy config files into the provider-agnostic <c>records</c> schema.
/// Supported inputs:
/// <list type="bullet">
/// <item>mittwald-ddns flat config (<c>api_key</c> + <c>domains</c>)</item>
/// <item>mittwald-ddns account config (<c>accounts[].domains</c>)</item>
/// <item>dyndns.py Cloudflare config (<c>accounts[].api_token</c> + <c>zones</c>)</item>
/// </list>
/// Files that are already current are returned unchanged.
/// </summary>
public static class ConfigMigration
{
    public static string Migrate(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (node is not JsonObject root) return json;

        var accounts = root["accounts"] as JsonArray;
        if (accounts is null || accounts.Count == 0)
        {
            var legacyAccount = BuildLegacyRootAccount(root);
            if (legacyAccount is not null)
            {
                accounts = new JsonArray(legacyAccount);
                root["accounts"] = accounts;
            }
        }

        if (accounts is not null)
            for (var index = 0; index < accounts.Count; index++)
                if (accounts[index] is JsonObject account)
                    MigrateAccount(account, index);

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonObject? BuildLegacyRootAccount(JsonObject root)
    {
        var apiKey = FindString(root, "api_key", "ApiKey");
        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        var domains = FindArray(root, "domains", "Domains");
        if (domains is null) return null;

        NormalizeLegacyRootValue(root, "timeout", "Timeout");
        NormalizeLegacyRootValue(root, "global_webhook", "GlobalWebhook");
        NormalizeLegacyRootValue(root, "default_api_version", "DefaultApiVersion");

        var account = new JsonObject
        {
            ["type"] = ProviderTypes.Mittwald,
            ["name"] = "main",
            ["api_key"] = apiKey
        };

        var apiVersion = FindInt(root, "global_api_version", "GlobalApiVersion", "api_version", "ApiVersion");
        if (apiVersion is not null) account["api_version"] = apiVersion.Value;

        account["records"] = ConvertMittwaldDomains(domains);

        root.Remove("api_key");
        root.Remove("ApiKey");
        root.Remove("domains");
        root.Remove("Domains");
        root.Remove("global_api_version");
        root.Remove("GlobalApiVersion");

        return account;
    }

    private static void MigrateAccount(JsonObject account, int index)
    {
        var explicitType = FindString(account, "type", "Type");
        var hasCloudflareShape = FindString(account, "api_token", "ApiToken") is not null
                                 || FindArray(account, "zones", "Zones") is not null;
        var type = ProviderTypes.Normalize(explicitType ?? (hasCloudflareShape ? ProviderTypes.Cloudflare : ProviderTypes.Mittwald));
        account["type"] = type;

        var name = FindString(account, "name", "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = string.Equals(type, ProviderTypes.Cloudflare, StringComparison.Ordinal)
                ? $"cloudflare-{index + 1}"
                : $"account-{index + 1}";
            account["name"] = name;
        }

        var records = FindArray(account, "records", "Records");

        if (string.Equals(type, ProviderTypes.Cloudflare, StringComparison.OrdinalIgnoreCase))
        {
            var token = FindString(account, "api_token", "ApiToken");
            if (!string.IsNullOrWhiteSpace(token) && string.IsNullOrWhiteSpace(FindString(account, "api_key", "ApiKey")))
                account["api_key"] = token;

            records ??= ConvertCloudflareZones(FindArray(account, "zones", "Zones"));

            account.Remove("api_token");
            account.Remove("ApiToken");
            account.Remove("zones");
            account.Remove("Zones");
        }
        else
        {
            records ??= ConvertMittwaldDomains(FindArray(account, "domains", "Domains"));

            var apiVersion = FindInt(account, "api_version", "ApiVersion");
            if (apiVersion is not null) account["api_version"] = apiVersion.Value;

            account.Remove("domains");
            account.Remove("Domains");
        }

        if (records is not null) account["records"] = records;
        account.Remove("Records");
    }

    private static JsonArray ConvertMittwaldDomains(JsonArray? domains)
    {
        var records = new JsonArray();
        if (domains is null) return records;

        foreach (var domainNode in domains.OfType<JsonObject>())
        {
            var domain = FindString(domainNode, "domain", "Domain");
            if (string.IsNullOrWhiteSpace(domain)) continue;

            var record = new JsonObject { ["domain"] = domain };
            Copy(domainNode, record, "zone_id", "id", "Id");
            Copy(domainNode, record, "project_id", "project_id", "ProjectId");
            records.Add(record);
        }

        return records;
    }

    private static JsonArray ConvertCloudflareZones(JsonArray? zones)
    {
        var records = new JsonArray();
        if (zones is null) return records;

        foreach (var zoneNode in zones.OfType<JsonObject>())
        {
            var zoneId = FindString(zoneNode, "zone_id", "ZoneId", "zoneid", "id", "Id");
            if (FindArray(zoneNode, "records", "Records") is not { } zoneRecords) continue;

            foreach (var recordNode in zoneRecords.OfType<JsonObject>())
            {
                var domain = FindString(recordNode, "domain", "Domain", "name", "Name");
                if (string.IsNullOrWhiteSpace(domain)) continue;

                var record = new JsonObject { ["domain"] = domain };
                if (!string.IsNullOrWhiteSpace(zoneId)) record["zone_id"] = zoneId;
                Copy(recordNode, record, "record_id", "record_id", "RecordId", "id", "Id");
                Copy(recordNode, record, "webhook", "webhook", "Webhook");
                Copy(recordNode, record, "proxied", "proxied", "Proxied");
                Copy(recordNode, record, "ttl", "ttl", "Ttl");
                records.Add(record);
            }
        }

        return records;
    }

    private static void Copy(JsonObject source, JsonObject destination, string targetName, params string[] sourceNames)
    {
        foreach (var sourceName in sourceNames)
        {
            if (source.TryGetPropertyValue(sourceName, out var value) && value is not null)
            {
                destination[targetName] = value.DeepClone();
                return;
            }
        }
    }

    private static void NormalizeLegacyRootValue(JsonObject root, string targetName, params string[] sourceNames)
    {
        if (root.ContainsKey(targetName)) return;

        foreach (var sourceName in sourceNames)
        {
            if (!root.TryGetPropertyValue(sourceName, out var value) || value is null) continue;

            root[targetName] = value.DeepClone();
            root.Remove(sourceName);
            return;
        }
    }

    private static JsonNode? FindValue(JsonObject node, params string[] names)
    {
        foreach (var property in node)
        {
            if (!names.Contains(property.Key, StringComparer.OrdinalIgnoreCase)) continue;
            if (property.Value is not null) return property.Value;
        }

        return null;
    }

    private static string? FindString(JsonObject node, params string[] names)
    {
        return FindValue(node, names) is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }

    private static int? FindInt(JsonObject node, params string[] names)
    {
        return FindValue(node, names) is JsonValue value && value.TryGetValue<int>(out var number)
            ? number
            : null;
    }

    private static JsonArray? FindArray(JsonObject node, params string[] names)
    {
        return FindValue(node, names) as JsonArray;
    }
}
