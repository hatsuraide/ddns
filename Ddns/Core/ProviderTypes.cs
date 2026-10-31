namespace Ddns.Core;

/// <summary>
/// Well-known provider identifiers as they appear in <c>accounts[].type</c>.
/// </summary>
public static class ProviderTypes
{
    public const string Mittwald = "mittwald";
    public const string Cloudflare = "cloudflare";

    public static readonly IReadOnlyList<string> All = [Mittwald, Cloudflare];

    public static string Normalize(string? type)
    {
        return string.IsNullOrWhiteSpace(type) ? Mittwald : type.Trim().ToLowerInvariant();
    }

    public static bool IsSupported(string? type)
    {
        return All.Contains(Normalize(type), StringComparer.OrdinalIgnoreCase);
    }
}
