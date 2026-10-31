using Ddns.Cli.Tui;

namespace Ddns.Tests.Cli;

public sealed class TuiSessionTests
{
    private static readonly List<TuiItem> Items =
    [
        new("one"), new("two"), new("three")
    ];

    [Fact]
    public void Select_NavigatesWithArrowAndVimKeys()
    {
        var session = CreateSession(Key(ConsoleKey.DownArrow), Key(ConsoleKey.J), Key(ConsoleKey.Enter));

        var selected = session.Select("Title", Items, isRoot: false);

        Assert.Equal(2, selected);
    }

    [Fact]
    public void Select_ReturnsNullWhenCancelled()
    {
        var session = CreateSession(Key(ConsoleKey.DownArrow), Key(ConsoleKey.Escape));

        Assert.Null(session.Select("Title", Items, isRoot: false));
    }

    [Fact]
    public void Select_QOnlyQuitsAtRoot()
    {
        var nested = CreateSession(Key(ConsoleKey.Q), Key(ConsoleKey.Enter));
        Assert.Equal(0, nested.Select("Title", Items, isRoot: false));

        var root = CreateSession(Key(ConsoleKey.Q));
        Assert.Null(root.Select("Title", Items, isRoot: true));
    }

    [Fact]
    public void Select_ClampsSelectionAtBoundaries()
    {
        var session = CreateSession(
            Key(ConsoleKey.UpArrow),
            Key(ConsoleKey.PageDown),
            Key(ConsoleKey.End),
            Key(ConsoleKey.Enter));

        Assert.Equal(2, session.Select("Title", Items, isRoot: false));
    }

    [Fact]
    public void Select_ReturnsNullWhenThereAreNoItems()
    {
        var session = CreateSession();

        Assert.Null(session.Select("Title", [], isRoot: true));
    }

    [Fact]
    public void SelectMany_TogglesWithSpaceAndConfirmsWithEnter()
    {
        var session = CreateSession(
            Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.DownArrow),
            Key(ConsoleKey.Spacebar),
            Key(ConsoleKey.Enter));

        var selected = session.SelectMany("Select", Items);

        Assert.Equal([0, 2], selected);
    }

    [Fact]
    public void SelectMany_SeedsPreselectedItems()
    {
        var session = CreateSession(Key(ConsoleKey.Enter));

        Assert.Equal([1], session.SelectMany("Select", Items, [1]));
    }

    [Fact]
    public void SelectMany_ReturnsNullWhenCancelled()
    {
        var session = CreateSession(Key(ConsoleKey.Spacebar), Key(ConsoleKey.Escape));

        Assert.Null(session.SelectMany("Select", Items));
    }

    [Fact]
    public void Select_DoesNothingWhenDisabled()
    {
        var writer = new StringWriter();
        var session = new TuiSession(writer, enabled: false);

        Assert.Null(session.Select("Title", Items, isRoot: true));
        Assert.Null(session.SelectMany("Title", Items));
        Assert.False(session.IsActive);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void AlternateScreen_EntersAndLeavesExactlyOnce()
    {
        var writer = new StringWriter();
        using var session = new TuiSession(writer, enabled: true, readKey: () => Key(ConsoleKey.Enter), size: () => (80, 24));

        session.EnterAlternateScreen();
        Assert.True(session.IsActive);

        session.EnterAlternateScreen();
        session.Suspend();
        Assert.False(session.IsActive);

        session.Resume();
        Assert.True(session.IsActive);

        session.Dispose();
        session.Dispose();
        Assert.False(session.IsActive);

        var output = writer.ToString();
        Assert.Contains("\x1b[?1049h", output);
        Assert.Contains("\x1b[?1049l", output);
        Assert.Equal(2, CountOccurrences(output, "\x1b[?1049h"));
        Assert.Equal(2, CountOccurrences(output, "\x1b[?1049l"));
    }

    [Fact]
    public void Alert_WritesMessageAndWaitsForKey()
    {
        var readCount = 0;
        var writer = new StringWriter();
        using var session = new TuiSession(
            writer,
            enabled: true,
            readKey: () => { readCount++; return Key(ConsoleKey.Enter); },
            size: () => (80, 24));

        session.EnterAlternateScreen();
        session.Alert("Saved", error: true);

        Assert.Equal(1, readCount);
        Assert.Contains("Saved", writer.ToString());
        Assert.Contains("\x1b[31m", writer.ToString());
    }

    private static TuiSession CreateSession(params ConsoleKeyInfo[] keys)
    {
        var queue = new Queue<ConsoleKeyInfo>(keys);
        return new TuiSession(
            new StringWriter(),
            enabled: true,
            readKey: () => queue.Count > 0 ? queue.Dequeue() : Key(ConsoleKey.Escape),
            size: () => (80, 24));
    }

    private static ConsoleKeyInfo Key(ConsoleKey key)
    {
        return new ConsoleKeyInfo('\0', key, false, false, false);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = value.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = value.IndexOf(needle, index + needle.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
