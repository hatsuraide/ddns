namespace Ddns.Cli.Tui;

/// <summary>
/// One selectable row in a <see cref="TuiSession"/> menu.
/// </summary>
/// <param name="Label">Primary text.</param>
/// <param name="Description">Optional secondary text, appended after the label.</param>
/// <param name="IsSubmenu">Marks rows that open another menu (rendered with <c>&gt;</c>).</param>
public sealed record TuiItem(string Label, string? Description = null, bool IsSubmenu = false)
{
    public string DisplayText => string.IsNullOrEmpty(Description) ? Label : $"{Label} - {Description}";
}
