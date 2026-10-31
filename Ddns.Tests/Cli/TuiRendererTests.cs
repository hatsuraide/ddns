using System.Text.RegularExpressions;
using Ddns.Cli.Tui;

namespace Ddns.Tests.Cli;

public sealed partial class TuiRendererTests
{
    private static readonly string[] Items4 =
    [
        "one", "two", "three", "four"
    ];

    [Fact]
    public void Render_DrawsConsistentBoxWidth()
    {
        var frame = TuiRenderer.Render(60, 20, "Title", ToItems(Items4), 0, 0, isRoot: true);
        var lines = frame.Split("\r\n").Select(StripAnsi).ToList();

        Assert.StartsWith("┌", lines[0]);
        Assert.EndsWith("┘", lines[^1]);

        foreach (var line in lines)
            Assert.Equal(60, line.Length);
    }

    [Fact]
    public void Render_HighlightsSelectedRowOnly()
    {
        var frame = TuiRenderer.Render(40, 20, "Title", ToItems(Items4), 1, 0, isRoot: false);
        var rows = frame.Split("\r\n").Where(line => line.Contains('►')).ToList();

        var row = Assert.Single(rows);
        Assert.Contains("► two", StripAnsi(row));
        Assert.Contains("\x1b[7m", row);
    }

    [Fact]
    public void Render_MarksSubmenusAndRootHint()
    {
        var items = new List<TuiItem>
        {
            new("Accounts", "3 accounts", IsSubmenu: true),
            new("Exit")
        };

        var frame = StripAnsi(TuiRenderer.Render(60, 20, "DDNS Config", items, 0, 0, isRoot: true));

        Assert.Contains("Accounts - 3 accounts >", frame);
        Assert.Contains("q/Esc Quit", frame);
    }

    [Fact]
    public void Render_ShowsScrollIndicatorForLongLists()
    {
        var items = Enumerable.Range(1, 30).Select(index => new TuiItem($"item {index}")).ToList();
        var frame = StripAnsi(TuiRenderer.Render(40, 20, "Title", items, 0, 0, isRoot: false));

        Assert.Contains("1–10/30", frame);
        Assert.Contains("Esc Back", frame);
    }

    [Fact]
    public void Render_TruncatesLongLabels()
    {
        var items = new List<TuiItem> { new("x", new string('y', 200)) };
        var frame = StripAnsi(TuiRenderer.Render(40, 20, "Title", items, 0, 0, isRoot: false));

        Assert.Contains("…", frame);
    }

    [Fact]
    public void Render_ShowsCheckboxMarkersForMultiSelect()
    {
        var items = ToItems(Items4);
        var frame = StripAnsi(TuiRenderer.Render(
            40, 20, "Select", items, 0, 0, isRoot: false, checkedItems: new HashSet<int> { 0, 2 }));

        Assert.Contains("[x] one", frame);
        Assert.Contains("[ ] two", frame);
        Assert.Contains("[x] three", frame);
        Assert.Contains("Space Toggle", frame);
    }

    [Fact]
    public void Trim_ReplacesOverflowWithEllipsis()
    {
        Assert.Equal("abc", TuiRenderer.Trim("abc", 3));
        Assert.Equal("ab…", TuiRenderer.Trim("abcdef", 3));
        Assert.Equal(string.Empty, TuiRenderer.Trim("abc", 0));
    }

    [Theory]
    [InlineData(24, 5, 16)]
    [InlineData(4, 3, 1)]
    [InlineData(20, 30, 10)]
    public void VisibleCount_ReservesChromeRowsAndClamps(int height, int itemCount, int expected)
    {
        Assert.Equal(expected, TuiRenderer.VisibleCount(height, itemCount));
    }

    [Fact]
    public void AdjustScroll_KeepsSelectionVisible()
    {
        Assert.Equal(0, TuiRenderer.AdjustScroll(2, 0, 5, 10));
        Assert.Equal(3, TuiRenderer.AdjustScroll(7, 0, 5, 10));
        Assert.Equal(2, TuiRenderer.AdjustScroll(2, 5, 5, 10));
        Assert.Equal(0, TuiRenderer.AdjustScroll(0, 0, 5, 0));
    }

    private static List<TuiItem> ToItems(IEnumerable<string> labels)
    {
        return labels.Select(label => new TuiItem(label)).ToList();
    }

    private static string StripAnsi(string value)
    {
        return AnsiPattern().Replace(value, string.Empty);
    }

    [GeneratedRegex("\u001b\\[[0-9;?]*[a-zA-Z]")]
    private static partial Regex AnsiPattern();
}
