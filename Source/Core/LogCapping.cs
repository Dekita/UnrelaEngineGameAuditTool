namespace DekUnrealGameAudit.Core;

/// <summary>Caps how many per-item lines a GUI log view echoes for one category of a comparison result.
/// Appending one WPF TextBox line (plus a ScrollToEnd layout pass) per changed/added/removed asset doesn't
/// scale to real comparisons, which can have tens or hundreds of thousands of entries - the exported
/// JSON/Markdown report always has the full list regardless of what's echoed to the on-screen log.</summary>
public static class LogCapping {
    public const int DefaultMaxLines = 200;

    /// <summary>Formats and yields at most <paramref name="max"/> items, then one summary line if there were
    /// more. Lazy - never formats or holds more than <paramref name="max"/> items at once.</summary>
    public static IEnumerable<string> Cap<T>(IReadOnlyCollection<T> items, Func<T, string> format, int max = DefaultMaxLines) {
        var shown = 0;
        foreach (var item in items) {
            if (shown >= max)
                break;
            yield return format(item);
            shown++;
        }
        if (items.Count > max)
            yield return $"  ... and {items.Count - max} more - see the full exported report.";
    }
}
