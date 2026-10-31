namespace Ddns.Core;

/// <summary>
/// A DNS record that a provider discovered through its API and that can be
/// selected in the interactive config workspace.
/// </summary>
public sealed record DiscoveredTarget(
    string Domain,
    string? ZoneId = null,
    string? RecordId = null,
    string? ProjectId = null)
{
    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(ZoneId) ? Domain : $"{Domain} (zone {ZoneId})";
    }
}
