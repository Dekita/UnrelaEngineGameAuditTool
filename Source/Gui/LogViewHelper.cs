using System.Windows;
using System.Windows.Controls;

namespace DekUnrealGameAudit.Gui;

/// <summary>Shared behavior for the read-only logs used throughout the app.</summary>
internal static class LogViewHelper {
    public static void Append(TextBox logBox, ScrollViewer scrollViewer, string line) {
        // Preserve the reader's position if they have scrolled up to inspect earlier output.
        var wasAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 2;
        logBox.AppendText(line + Environment.NewLine);
        if (wasAtBottom)
            scrollViewer.ScrollToEnd();
    }

    public static void CopyAll(TextBox logBox) {
        if (logBox.Text.Length > 0)
            Clipboard.SetText(logBox.Text);
    }
}
