namespace Ddns.Core;

/// <summary>
/// Outcome of a single provider update. <see cref="Changed"/> is false when the
/// provider could determine that the record already points at the new address.
/// </summary>
public sealed record DnsUpdateResult(bool Changed, string? PreviousAddress, string Message)
{
    public static DnsUpdateResult Unchanged(string? currentAddress = null)
    {
        return new DnsUpdateResult(false, currentAddress, "unchanged");
    }

    public static DnsUpdateResult Updated(string? previousAddress = null)
    {
        return new DnsUpdateResult(true, previousAddress, "updated");
    }
}
