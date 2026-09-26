using System.Windows.Automation;
using System.Windows.Controls;

namespace DekUnrealGameAudit.Gui;

/// <summary>Most views set their status TextBlock's <see cref="AutomationProperties.LiveSettingProperty"/> to
/// Polite once in XAML, since they only update it a handful of times per run - a screen reader announcing each
/// one is exactly the "announce completion, not every log entry" behavior UI-08 asks for. Hash Game Files is the
/// exception: its progress text updates on every coalesced per-asset tick (still up to ~10/second on a large
/// scan), so a permanently-Polite live region there would read out a flood of "42% - 12.3s elapsed" instead of
/// just the start/finish. These two helpers let that one view toggle live-ness per update instead.</summary>
public static class AccessibilityAnnouncer {
    /// <summary>Use for a milestone worth a screen reader announcement - operation started, cancelled, failed or
    /// completed - not for a value that updates rapidly.</summary>
    public static void Announce(TextBlock target, string text) {
        AutomationProperties.SetLiveSetting(target, AutomationLiveSetting.Polite);
        target.Text = text;
    }

    /// <summary>Use for a rapidly-updating value (e.g. per-asset progress) that would flood a screen reader if
    /// announced on every change.</summary>
    public static void UpdateSilently(TextBlock target, string text) {
        AutomationProperties.SetLiveSetting(target, AutomationLiveSetting.Off);
        target.Text = text;
    }
}
