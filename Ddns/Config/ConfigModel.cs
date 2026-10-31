using System.Text.Json.Serialization;
using Ddns.Core;

namespace Ddns.Config;

public sealed class ConfigModel
{
    public const string DefaultIpServiceUrl = PublicIpService.DefaultServiceUrl;

    [JsonPropertyName("global_webhook")] public string? GlobalWebhook { get; set; }

    /// <summary>Default mittwald API version for accounts without an explicit override.</summary>
    [JsonPropertyName("default_api_version")]
    public int DefaultApiVersion { get; set; } = 2;

    [JsonPropertyName("timeout")] public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    [JsonPropertyName("ip_service_url")]
    public string IpServiceUrl { get; set; } = DefaultIpServiceUrl;

    [JsonPropertyName("notify_on_change")]
    public bool NotifyOnChange { get; set; } = true;

    [JsonPropertyName("notify_on_error")]
    public bool NotifyOnError { get; set; } = true;

    [JsonPropertyName("accounts")] public List<ConfigAccount> Accounts { get; set; } = [];
}

public sealed class ConfigAccount
{
    /// <summary>Provider identifier, see <see cref="ProviderTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = ProviderTypes.Mittwald;

    /// <summary>
    /// Account label. For mittwald API v1 this is also the mittwald account
    /// name or numeric UID used to address the API.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>API key or API token. Used by every provider type.</summary>
    [JsonPropertyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [JsonPropertyName("api_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ApiVersion { get; set; }

    [JsonPropertyName("webhook")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Webhook { get; set; }

    [JsonPropertyName("records")] public List<ConfigRecord> Records { get; set; } = [];
}

public sealed class ConfigRecord
{
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;

    /// <summary>DNS zone id (Cloudflare) or mittwald v2 DNS zone id.</summary>
    [JsonPropertyName("zone_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ZoneId { get; set; }

    /// <summary>Provider record id (Cloudflare).</summary>
    [JsonPropertyName("record_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RecordId { get; set; }

    /// <summary>mittwald project id, used to resolve a v2 DNS zone by domain.</summary>
    [JsonPropertyName("project_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProjectId { get; set; }

    /// <summary>Cloudflare proxy flag. Keeps the current setting when unset.</summary>
    [JsonPropertyName("proxied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Proxied { get; set; }

    /// <summary>Cloudflare TTL. Keeps the current setting when unset.</summary>
    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Ttl { get; set; }

    /// <summary>Per-record webhook override.</summary>
    [JsonPropertyName("webhook")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Webhook { get; set; }
}

public sealed class EncryptedConfig
{
    [JsonPropertyName("format")] public string Format { get; set; } = ConfigStore.EncryptedFormat;

    [JsonPropertyName("version")] public int Version { get; set; } = 1;

    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = ConfigStore.EncryptionAlgorithm;

    [JsonPropertyName("ciphertext")] public string Ciphertext { get; set; } = string.Empty;

    [JsonPropertyName("nonce")] public string Nonce { get; set; } = string.Empty;

    [JsonPropertyName("tag")] public string Tag { get; set; } = string.Empty;
}
