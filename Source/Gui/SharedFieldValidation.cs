using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Gui;

/// <summary>Live validity checks for the fields <see cref="ISharedFieldView"/> keeps synced across tabs, so a
/// field that's currently broken (a deleted folder, an unloadable scope-rules file, an unrecognized UE version)
/// is visibly flagged on every tab showing it - not just discovered once Run fails on whichever tab you happened
/// to click it from. AES key is deliberately not covered here - see AesKeyParsing's own doc comment for why
/// there's no format to check.</summary>
public static class SharedFieldValidation {
    /// <summary>Splits on newlines first so this also validates a multi-root Mods folder box (CODE-17) - every
    /// non-blank line must be an existing folder, not just the field's raw text as a whole. A single-line value
    /// (every other folder field) behaves exactly as before.</summary>
    public static bool IsExistingFolderOrBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(Directory.Exists);

    /// <summary>EGameNames.All is the exact list EGameComboBox already filters against - the same ~260
    /// CUE4Parse names, so "valid" here means "matches one of them exactly", not just "looks plausible".</summary>
    public static bool IsKnownUeVersionOrBlank(string value) =>
        string.IsNullOrWhiteSpace(value) || EGameNames.All.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reuses ScopeRulesLoader.Load - the exact same preflight check Run already runs before a scan
    /// starts - rather than a separate, possibly-diverging notion of what makes a scope-rules file valid.</summary>
    public static bool IsLoadableScopeRulesPathOrBlank(string value) {
        try {
            ScopeRulesLoader.Load(value);
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>Mirrors App.xaml's NexusOrangeBrush (#FB923C) - the app's one existing "pay attention" color,
    /// already used for ConflictText/ResultWarningsText - rather than a resource lookup that has to work
    /// reliably as early as Initialize()/ApplyFromSettings(), before a view is necessarily in a live visual tree.</summary>
    private static readonly Brush InvalidFieldBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0x92, 0x3C));

    /// <summary>A plain instance-level BorderBrush/BorderThickness override, not a Style/Template - never
    /// touches the control's WPF-UI theming, exactly like this session's Height/Padding/FontSize/Cursor
    /// instance overrides (an implicit Style with no BasedOn replaces a control's whole template; a local DP
    /// value on the instance does not).</summary>
    public static void SetFieldValid(this Control control, bool isValid) {
        if (isValid) {
            control.ClearValue(Control.BorderBrushProperty);
            control.ClearValue(Control.BorderThicknessProperty);
        } else {
            control.BorderBrush = InvalidFieldBrush;
            control.BorderThickness = new Thickness(2);
        }
    }
}
