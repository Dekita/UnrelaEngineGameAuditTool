using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Gui;

/// <summary>Wires an editable ComboBox to live-filter CUE4Parse's ~260 EGame names as the user types, rather
/// than making them scroll one long alphabetical list to find the one they want.</summary>
public static class EGameComboBox {
    public static void Attach(ComboBox combo) {
        combo.ItemsSource = EGameNames.All;
        combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => {
            var text = combo.Text;
            var filtered = string.IsNullOrEmpty(text)
                ? EGameNames.All
                : EGameNames.All.Where(n => n.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
            // If the text is already an exact match (e.g. right after a profile switch fills in a saved,
            // valid value), filtering down to just that one item leaves nothing to browse to if the user
            // opens the dropdown wanting a different version - show the full list instead.
            combo.ItemsSource = filtered.Count == 1 && filtered[0].Equals(text, StringComparison.OrdinalIgnoreCase)
                ? EGameNames.All
                : filtered;
        }));
        combo.Loaded += (_, _) => PropagateNameToEditableTextBox(combo);
    }

    /// <summary>An editable ComboBox's own AutomationPeer resolves its Name correctly from
    /// AutomationProperties.Name/LabeledBy, but keyboard focus while typing actually lands on its internal
    /// PART_EditableTextBox template part - a separate AutomationElement that doesn't inherit that Name and
    /// UI-tests confirmed reports a blank one instead. Mirroring the resolved name onto that part directly is
    /// what actually reaches a screen reader while the user is typing.</summary>
    private static void PropagateNameToEditableTextBox(ComboBox combo) {
        combo.ApplyTemplate();
        if (combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox editableTextBox)
            return;

        var name = AutomationProperties.GetName(combo);
        if (string.IsNullOrEmpty(name) && AutomationProperties.GetLabeledBy(combo) is TextBlock label)
            name = label.Text;

        if (!string.IsNullOrEmpty(name))
            AutomationProperties.SetName(editableTextBox, name);
    }
}
