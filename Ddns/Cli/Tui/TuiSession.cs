using System.Text;

namespace Ddns.Cli.Tui;

/// <summary>
/// Drives <see cref="TuiRenderer"/> on the real terminal: alternate screen
/// buffer, hidden cursor and single-key navigation. The key reader and size
/// provider are injectable so the loop can be exercised in tests.
/// </summary>
public sealed class TuiSession : IDisposable
{
    private const string EnterSequence = "\x1b[?1049h\x1b[?25l\x1b[2J\x1b[H";
    private const string LeaveSequence = "\x1b[?25h\x1b[?1049l";

    private readonly TextWriter _output;
    private readonly Func<ConsoleKeyInfo> _readKey;
    private readonly Func<(int Width, int Height)> _size;

    private bool _altScreen;
    private bool _disposed;

    public TuiSession(
        TextWriter output,
        bool enabled,
        Func<ConsoleKeyInfo>? readKey = null,
        Func<(int Width, int Height)>? size = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        _output = output;
        Enabled = enabled;
        _readKey = readKey ?? (() => Console.ReadKey(intercept: true));
        _size = size ?? (() => (SafeWindowWidth(), SafeWindowHeight()));
    }

    public bool Enabled { get; }

    /// <summary>True while the alternate screen buffer is active.</summary>
    public bool IsActive => Enabled && _altScreen && !_disposed;

    /// <summary>
    /// The TUI needs an interactive terminal. Redirected input or output (CI,
    /// cron, <c>docker compose run</c> without a TTY) falls back to plain prompts.
    /// </summary>
    public static bool IsTerminalAvailable()
    {
        try
        {
            return !Console.IsInputRedirected && !Console.IsOutputRedirected;
        }
        catch
        {
            return false;
        }
    }

    public void EnterAlternateScreen()
    {
        if (!Enabled || _altScreen || _disposed) return;

        Write(EnterSequence);
        _altScreen = true;
    }

    public void ExitAlternateScreen()
    {
        if (!_altScreen) return;

        Write(LeaveSequence);
        _altScreen = false;
    }

    /// <summary>Temporarily restores the normal screen, e.g. for a text prompt.</summary>
    public void Suspend()
    {
        ExitAlternateScreen();
    }

    /// <summary>Re-enters the alternate screen after <see cref="Suspend"/>.</summary>
    public void Resume()
    {
        if (_disposed || !Enabled || _altScreen) return;

        EnterAlternateScreen();
    }

    /// <summary>
    /// Shows a single-select menu. Returns the selected index, or null when the
    /// user backs out (<c>Esc</c>, <c>Backspace</c>, or <c>q</c> at the root).
    /// </summary>
    public int? Select(string title, IReadOnlyList<TuiItem> items, bool isRoot)
    {
        if (!Enabled || items.Count == 0) return null;

        var selected = 0;
        var scroll = 0;

        while (true)
        {
            var (visible, adjustedScroll, _) = RenderFrame(title, items, selected, scroll, isRoot, null);
            scroll = adjustedScroll;

            var key = ReadKey();
            if (key is null) return null;

            switch (Classify(key.Value, isRoot))
            {
                case TuiKey.Up:
                    if (selected > 0) selected--;
                    break;
                case TuiKey.Down:
                    if (selected < items.Count - 1) selected++;
                    break;
                case TuiKey.PageUp:
                    selected = Math.Max(0, selected - Math.Max(1, visible - 1));
                    break;
                case TuiKey.PageDown:
                    selected = Math.Min(items.Count - 1, selected + Math.Max(1, visible - 1));
                    break;
                case TuiKey.Home:
                    selected = 0;
                    break;
                case TuiKey.End:
                    selected = items.Count - 1;
                    break;
                case TuiKey.Accept:
                    return selected;
                case TuiKey.Cancel:
                    return null;
            }
        }
    }

    /// <summary>
    /// Shows a multi-select menu. <c>Space</c> toggles rows, <c>Enter</c> confirms
    /// and <c>Esc</c> cancels (returns null).
    /// </summary>
    public IReadOnlyList<int>? SelectMany(
        string title,
        IReadOnlyList<TuiItem> items,
        IEnumerable<int>? preselected = null,
        bool isRoot = false)
    {
        if (!Enabled || items.Count == 0) return null;

        var checkedItems = new HashSet<int>(preselected ?? []);
        var selected = 0;
        var scroll = 0;

        while (true)
        {
            var (visible, adjustedScroll, _) = RenderFrame(title, items, selected, scroll, isRoot, checkedItems);
            scroll = adjustedScroll;

            var key = ReadKey();
            if (key is null) return null;

            switch (Classify(key.Value, isRoot))
            {
                case TuiKey.Up:
                    if (selected > 0) selected--;
                    break;
                case TuiKey.Down:
                    if (selected < items.Count - 1) selected++;
                    break;
                case TuiKey.PageUp:
                    selected = Math.Max(0, selected - Math.Max(1, visible - 1));
                    break;
                case TuiKey.PageDown:
                    selected = Math.Min(items.Count - 1, selected + Math.Max(1, visible - 1));
                    break;
                case TuiKey.Home:
                    selected = 0;
                    break;
                case TuiKey.End:
                    selected = items.Count - 1;
                    break;
                case TuiKey.Toggle:
                    if (!checkedItems.Remove(selected)) checkedItems.Add(selected);
                    break;
                case TuiKey.Accept:
                    return checkedItems.Order().ToList();
                case TuiKey.Cancel:
                    return null;
            }
        }
    }

    /// <summary>Shows a centered message box and waits for a key press.</summary>
    public void Alert(string message, bool error = false)
    {
        if (!IsActive) return;

        var (width, height) = _size();
        var w = Math.Max(TuiRenderer.MinWidth, width);
        var h = Math.Max(TuiRenderer.MinHeight, height);
        var boxWidth = Math.Min(Math.Max(30, message.Length + 2), w - 2);
        var text = TuiRenderer.Trim($" {message} ", boxWidth);
        var row = Math.Max(1, (h - 4) / 2 + 1);
        var column = Math.Max(1, (w - (boxWidth + 2)) / 2 + 1);
        var color = error ? "\x1b[31m" : "\x1b[36m";

        var builder = new StringBuilder();
        builder.Append($"\x1b[{row};{column}H{color}┌{new string('─', boxWidth)}┐\x1b[0m");
        builder.Append($"\x1b[{row + 1};{column}H{color}│\x1b[0m{text}{new string(' ', boxWidth - text.Length)}\x1b[{row + 1};{column + boxWidth + 1}H{color}│\x1b[0m");
        builder.Append($"\x1b[{row + 2};{column}H{color}└{new string('─', boxWidth)}┘\x1b[0m");
        var hint = " Press any key ";
        builder.Append($"\x1b[{row + 3};{column}H\x1b[2m{hint}\x1b[0m");

        Write(builder.ToString());
        _ = ReadKey();
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        ExitAlternateScreen();
    }

    private (int Visible, int Scroll, string Frame) RenderFrame(
        string title,
        IReadOnlyList<TuiItem> items,
        int selected,
        int scroll,
        bool isRoot,
        IReadOnlySet<int>? checkedItems)
    {
        var (width, height) = _size();
        var visible = TuiRenderer.VisibleCount(height, items.Count);
        var adjustedScroll = TuiRenderer.AdjustScroll(selected, scroll, visible, items.Count);
        var frame = TuiRenderer.Render(
            width, height, title, items, selected, adjustedScroll, isRoot, checkedItems);

        Write(frame);
        return (visible, adjustedScroll, frame);
    }

    private ConsoleKeyInfo? ReadKey()
    {
        try
        {
            return _readKey();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static TuiKey Classify(ConsoleKeyInfo key, bool isRoot)
    {
        var noModifiers = (key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) == 0;

        return key.Key switch
        {
            ConsoleKey.UpArrow => TuiKey.Up,
            ConsoleKey.K when noModifiers => TuiKey.Up,
            ConsoleKey.DownArrow => TuiKey.Down,
            ConsoleKey.J when noModifiers => TuiKey.Down,
            ConsoleKey.PageUp => TuiKey.PageUp,
            ConsoleKey.PageDown => TuiKey.PageDown,
            ConsoleKey.Home => TuiKey.Home,
            ConsoleKey.End => TuiKey.End,
            ConsoleKey.Enter => TuiKey.Accept,
            ConsoleKey.Spacebar => TuiKey.Toggle,
            ConsoleKey.Escape => TuiKey.Cancel,
            ConsoleKey.Backspace => TuiKey.Cancel,
            // Only the root menu quits on q; in submenus it is just a letter.
            ConsoleKey.Q when noModifiers && isRoot => TuiKey.Cancel,
            _ => TuiKey.None
        };
    }

    private void Write(string text)
    {
        try
        {
            _output.Write(text);
            _output.Flush();
        }
        catch (IOException)
        {
            // The terminal went away; nothing sensible to do.
        }
    }

    private static int SafeWindowWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch
        {
            return 80;
        }
    }

    private static int SafeWindowHeight()
    {
        try
        {
            return Console.WindowHeight;
        }
        catch
        {
            return 24;
        }
    }

    private enum TuiKey
    {
        None,
        Up,
        Down,
        PageUp,
        PageDown,
        Home,
        End,
        Accept,
        Toggle,
        Cancel
    }
}
