namespace Ddns.Cli.Tui;

/// <summary>A menu entry that carries a value for the caller.</summary>
public sealed record MenuChoice<T>(T Value, string Label, string? Description = null, bool IsSubmenu = false)
{
    public TuiItem ToItem()
    {
        return new TuiItem(Label, Description, IsSubmenu);
    }

    public override string ToString()
    {
        return string.IsNullOrEmpty(Description) ? Label : $"{Label} - {Description}";
    }
}
