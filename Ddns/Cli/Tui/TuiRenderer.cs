using System.Text;

namespace Ddns.Cli.Tui;

/// <summary>
/// Pure renderer for the boxed terminal menu. The visual language is borrowed
/// from hatsuraide/LSAC's TUI: an alt-screen box with a highlighted row, a
/// scroll position indicator and a key hint bar.
/// </summary>
public static class TuiRenderer
{
    public const int MinWidth = 20;
    public const int MinHeight = 5;

    /// <summary>Rows used by borders, title, padding and the hint bar.</summary>
    public const int ChromeRows = 8;

    /// <summary>Extra rows consumed by the scroll indicator (separator + info).</summary>
    public const int ScrollChromeRows = 2;

    private const string RootHint = " ↑↓/jk Move   Enter Select   q/Esc Quit";
    private const string BackHint = " ↑↓/jk Move   Enter Select   Esc Back";
    private const string MultiHint = " ↑↓/jk Move   Space Toggle   Enter Confirm   Esc Cancel";

    public static int VisibleCount(int height, int itemCount)
    {
        var safeHeight = Math.Max(MinHeight, height);
        var baseVisible = Math.Max(1, safeHeight - ChromeRows);

        return itemCount > baseVisible
            ? Math.Max(1, safeHeight - ChromeRows - ScrollChromeRows)
            : baseVisible;
    }

    public static int AdjustScroll(int selected, int scroll, int visible, int itemCount)
    {
        if (itemCount == 0) return 0;
        if (selected < scroll) return selected;
        if (selected >= scroll + visible) return selected - visible + 1;

        return scroll;
    }

    public static string Trim(string value, int max)
    {
        if (max <= 0) return string.Empty;
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }

    public static string Render(
        int width,
        int height,
        string title,
        IReadOnlyList<TuiItem> items,
        int selected,
        int scroll,
        bool isRoot,
        IReadOnlySet<int>? checkedItems = null)
    {
        var w = Math.Max(MinWidth, width);
        var h = Math.Max(MinHeight, height);
        var inner = w - 2;
        var multiSelect = checkedItems is not null;
        var visible = VisibleCount(h, items.Count);
        var needsScroll = items.Count > Math.Max(1, h - ChromeRows);

        var builder = new StringBuilder(w * h * 2);
        builder.Append("\x1b[H\x1b[0m");

        // top border + title
        BoxLine(builder, '┌', '─', '┐', inner);
        var titleText = Trim($" {title} ", inner);
        builder.Append("│\x1b[1;36m").Append(titleText).Append(' ', inner - titleText.Length).Append("\x1b[0m│\r\n");
        BoxLine(builder, '├', '─', '┤', inner);

        // padding row
        builder.Append("│").Append(' ', inner).Append("│\r\n");

        // items
        var end = Math.Min(scroll + visible, items.Count);
        for (var i = scroll; i < end; i++)
        {
            var item = items[i];
            var highlighted = i == selected;

            var prefix = highlighted ? "► " : "  ";
            if (multiSelect) prefix += checkedItems!.Contains(i) ? "[x] " : "[ ] ";

            var label = item.DisplayText;
            if (item.IsSubmenu) label += " >";

            // "│ " + prefix + label + padding + "│" must equal the box width.
            var labelWidth = Math.Max(0, inner - 1 - prefix.Length);
            label = Trim(label, labelWidth);
            var padding = Math.Max(0, labelWidth - label.Length);

            builder.Append("│ ");
            if (highlighted) builder.Append("\x1b[7m");
            builder.Append(prefix).Append(label).Append(' ', padding);
            if (highlighted) builder.Append("\x1b[0m");
            builder.Append("│\r\n");
        }

        // fill empty item rows
        for (var i = end - scroll; i < visible; i++)
            builder.Append("│").Append(' ', inner).Append("│\r\n");

        // padding row
        builder.Append("│").Append(' ', inner).Append("│\r\n");

        // optional scroll indicator
        if (needsScroll)
        {
            var info = Trim($" {scroll + 1}–{Math.Min(scroll + visible, items.Count)}/{items.Count} ", inner);
            BoxLine(builder, '├', '─', '┤', inner);
            builder.Append("│\x1b[2m").Append(info).Append(' ', inner - info.Length).Append("\x1b[0m│\r\n");
        }

        // hint bar + bottom border
        BoxLine(builder, '├', '─', '┤', inner);
        var hint = Trim(multiSelect ? MultiHint : isRoot ? RootHint : BackHint, inner);
        builder.Append("│\x1b[2m").Append(hint).Append(' ', inner - hint.Length).Append("\x1b[0m│\r\n");

        // A trailing newline on the last terminal row scrolls the buffer by one
        // line, so the bottom border is intentionally emitted without \r\n.
        builder.Append('└').Append('─', Math.Max(0, inner)).Append('┘');

        // Erase anything to the right of and below the box.
        builder.Append("\x1b[J");

        return builder.ToString();
    }

    private static void BoxLine(StringBuilder builder, char left, char middle, char right, int inner)
    {
        builder.Append(left).Append(middle, Math.Max(0, inner)).Append(right).Append("\r\n");
    }
}
