namespace Ddns.Tests;

/// <summary>
/// Serializes tests that read or write process-wide environment variables.
/// </summary>
[CollectionDefinition(Name)]
public sealed class EnvironmentCollection
{
    public const string Name = "Environment";
}
