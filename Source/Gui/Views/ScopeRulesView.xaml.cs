using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DekUnrealGameAudit.Core;
using Microsoft.Win32;

namespace DekUnrealGameAudit.Gui.Views;

/// <summary>UI-10: the editor that turns <see cref="AppSettings.ScopeRulesPath"/> from a hand-edited JSON file
/// into something built through the GUI - the same shared field already read by Hash Game Files/Hash Mod
/// Files/Quick Actions, not a new one. See <see cref="ScopeRuleRow"/>/<see cref="PreviewRow"/> at the bottom for
/// the two small display-only models this view uses internally.</summary>
public partial class ScopeRulesView : UserControl, ISharedFieldView {
    private AppSettings _settings = null!;
    private readonly List<ScopeRuleRow> _rows = new();
    private List<PreviewRow> _allPreviewRows = new();
    private bool _externallyLocked;

    /// <summary>See HashGameFilesView.IsBusy for why MainWindow uses this - set only while a Preview scan is
    /// actually running (Save/Add/Remove are instant, in-memory operations, not "busy" in that sense).</summary>
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    /// <summary>Mirrors HashGameFilesView/HashModFilesView/QuickActionsView's own scope-rules field-sync
    /// mechanism (see MainWindow.xaml.cs's OnSharedFieldChanged) so this view's file picker/New... stays the
    /// single place a rules file is "born" without the other views showing a stale path until their next tab
    /// switch.</summary>
    public event EventHandler<SharedFieldChangedEventArgs>? SharedFieldChanged;

    private void SetBusy(bool busy) {
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
        UpdateEditingEnabled();
    }

    public ScopeRulesView() {
        InitializeComponent();
        PreviewGroupFilterCombo.ItemsSource = new[] { "All", "Game", "Mod", "Ignore", "Unsupported/Unreadable" };
        PreviewGroupFilterCombo.SelectedIndex = 0;
        ScopeRulesPathBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.ScopeRulesPath, ScopeRulesPathBox.Text.Trim()));
            ScopeRulesPathBox.SetFieldValid(SharedFieldValidation.IsLoadableScopeRulesPathOrBlank(ScopeRulesPathBox.Text));
        };
        ScopeRulesPathBox.LostFocus += (_, _) => LoadRulesFromPath();
    }

    public void Initialize(AppSettings settings) {
        _settings = settings;
        ApplyFromSettings();
    }

    public void ApplyFromSettings() {
        ScopeRulesPathBox.Text = _settings.ScopeRulesPath ?? "";
        LoadRulesFromPath();
    }

    public void CaptureToSettings() {
        _settings.ScopeRulesPath = ScopeRulesPathBox.Text.Trim();
    }

    /// <summary>Applies a shared field pushed from another view (see ISharedFieldView) - MainWindow's own
    /// re-entrancy guard already stops this from re-raising SharedFieldChanged back at its source, so the
    /// views can't ping-pong. This is the only field ScopeRulesView shares.</summary>
    public void SetSharedField(string fieldName, string value) {
        if (fieldName != SharedField.ScopeRulesPath)
            return;
        ScopeRulesPathBox.Text = value;
        LoadRulesFromPath();
    }

    private void LoadRulesFromPath() {
        var path = ScopeRulesPathBox.Text.Trim();
        _rows.Clear();
        _allPreviewRows = new();
        PreviewListView.ItemsSource = null;
        PreviewCountsText.Text = "";

        if (string.IsNullOrWhiteSpace(path)) {
            SaveStatusText.Text = "";
        } else if (!File.Exists(path)) {
            SaveStatusText.Text = "File doesn't exist yet - click Save Rules to create it, or New... for a fresh one.";
        } else {
            try {
                var ruleSet = ScopeRulesLoader.Load(path);
                foreach (var rule in ruleSet!.Rules)
                    _rows.Add(new ScopeRuleRow { Path = rule.Path, IsFolder = rule.IsFolder, Action = rule.Action });
                SaveStatusText.Text = $"Loaded {_rows.Count} rule(s).";
            } catch (Exception ex) {
                SaveStatusText.Text = $"Could not load: {ex.Message}";
            }
        }

        RefreshRulesList();
        RevalidateRules();
    }

    private void RefreshRulesList() {
        RulesListView.ItemsSource = null;
        RulesListView.ItemsSource = _rows;
        RemoveRuleButton.IsEnabled = false;
        EmptyRulesHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private List<ScopeRule> ToScopeRules() =>
        _rows.Select(r => new ScopeRule { Path = r.Path, IsFolder = r.IsFolder, Action = r.Action }).ToList();

    /// <summary>Re-checks the in-memory rules against the same validation a real scan would run
    /// (ScopeClassifier.Validate, via ScopeRulesLoader.Load elsewhere) after every edit - "surface conflicts...
    /// before running" rather than only failing once a Hash Game/Mod Files run actually starts.</summary>
    private void RevalidateRules() {
        try {
            ScopeClassifier.Validate(new ScopeRuleSet { Rules = ToScopeRules() });
            ConflictText.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(ScopeRulesPathBox.Text.Trim());
        } catch (Exception ex) {
            ConflictText.Text = ex.Message;
            ConflictText.Visibility = Visibility.Visible;
            SaveButton.IsEnabled = false;
        }
    }

    /// <summary>Sets the per-row Action combo box's item list explicitly from code, once, the first time each
    /// row's combo box loads - see the DataTemplate's own comment in ScopeRulesView.xaml for why this is
    /// plain code instead of an ObjectDataProvider resource binding.</summary>
    private void RuleActionCombo_Loaded(object sender, RoutedEventArgs e) {
        if (sender is ComboBox combo && combo.ItemsSource == null)
            combo.ItemsSource = Enum.GetValues(typeof(ScopeAction));
    }

    private void RuleActionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        RevalidateRules();
        if (ConflictText.Visibility != Visibility.Visible)
            SaveStatusText.Text = "Unsaved changes.";
    }

    private void RulesListView_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        RemoveRuleButton.IsEnabled = RulesListView.SelectedItem != null;
    }

    private void AddFile_Click(object sender, RoutedEventArgs e) {
        var paksFolder = _settings.PaksFolder?.Trim();
        if (string.IsNullOrWhiteSpace(paksFolder)) {
            SaveStatusText.Text = "Set a Paks folder on Hash Game Files first - rules are relative to it.";
            return;
        }

        var dialog = new OpenFileDialog { Title = "Select a file inside the Paks folder" };
        if (Directory.Exists(paksFolder))
            dialog.InitialDirectory = paksFolder;
        if (dialog.ShowDialog() != true)
            return;

        var relative = TryGetRelativePath(paksFolder, dialog.FileName);
        if (relative == null) {
            SaveStatusText.Text = "The selected file must be inside the Paks folder.";
            return;
        }

        AddOrSelectRule(relative, isFolder: false, defaultAction: ScopeAction.Mod);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e) {
        var paksFolder = _settings.PaksFolder?.Trim();
        if (string.IsNullOrWhiteSpace(paksFolder)) {
            SaveStatusText.Text = "Set a Paks folder on Hash Game Files first - rules are relative to it.";
            return;
        }

        var dialog = new OpenFolderDialog { Title = "Select a folder inside the Paks folder" };
        if (Directory.Exists(paksFolder))
            dialog.InitialDirectory = paksFolder;
        if (dialog.ShowDialog() != true)
            return;

        var relative = TryGetRelativePath(paksFolder, dialog.FolderName);
        if (relative == null) {
            SaveStatusText.Text = "The selected folder must be inside the Paks folder.";
            return;
        }

        AddOrSelectRule(relative, isFolder: true, defaultAction: ScopeAction.Game);
    }

    /// <summary>Root-relative path for a file/folder rule, exactly like ScopeContainerScanner/ModsHasher already
    /// produce for real scans - or null if the picked item isn't actually inside the root (a different drive,
    /// or an ancestor of it), which no valid rule can express.</summary>
    private static string? TryGetRelativePath(string root, string fullPath) {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(fullPath)).Replace('\\', '/');
        return relative.StartsWith("..") || Path.IsPathRooted(relative) ? null : relative;
    }

    /// <summary>Adding the same path/type twice just selects the existing rule instead of creating a confusing
    /// duplicate - the row's own Action combo box is already how you'd change what it does.</summary>
    private void AddOrSelectRule(string path, bool isFolder, ScopeAction defaultAction) {
        var existing = _rows.FirstOrDefault(r => r.IsFolder == isFolder && string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing == null) {
            existing = new ScopeRuleRow { Path = path, IsFolder = isFolder, Action = defaultAction };
            _rows.Add(existing);
            SaveStatusText.Text = "Unsaved changes.";
        }

        RefreshRulesList();
        RulesListView.SelectedItem = existing;
        RevalidateRules();
    }

    private void RemoveRule_Click(object sender, RoutedEventArgs e) {
        if (RulesListView.SelectedItem is not ScopeRuleRow row)
            return;

        _rows.Remove(row);
        RefreshRulesList();
        RevalidateRules();
        SaveStatusText.Text = "Unsaved changes.";
    }

    private void Browse_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog { Title = "Select a scope rules JSON file", Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() != true)
            return;

        ScopeRulesPathBox.Text = dialog.FileName;
        LoadRulesFromPath();
    }

    private void New_Click(object sender, RoutedEventArgs e) {
        var dialog = new SaveFileDialog { Title = "Create a new scope rules file", Filter = "JSON files (*.json)|*.json", FileName = "scope-rules.json" };
        if (dialog.ShowDialog() != true)
            return;

        try {
            JsonUtil.WriteFile(dialog.FileName, new ScopeRuleSet());
        } catch (Exception ex) {
            SaveStatusText.Text = $"Could not create file: {ex.Message}";
            return;
        }

        ScopeRulesPathBox.Text = dialog.FileName;
        LoadRulesFromPath();
        SaveStatusText.Text = "New, empty scope rules file created.";
    }

    private void Save_Click(object sender, RoutedEventArgs e) {
        var path = ScopeRulesPathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(path)) {
            SaveStatusText.Text = "Set a file path first (Browse... or New...).";
            return;
        }

        var ruleSet = new ScopeRuleSet { Rules = ToScopeRules() };
        try {
            ScopeClassifier.Validate(ruleSet);
        } catch (Exception ex) {
            ConflictText.Text = ex.Message;
            ConflictText.Visibility = Visibility.Visible;
            SaveStatusText.Text = "Not saved - fix the conflict above first.";
            return;
        }

        try {
            JsonUtil.WriteFile(path, ruleSet);
            SaveStatusText.Text = $"Saved {ruleSet.Rules.Count} rule(s) to {Path.GetFullPath(path)}.";
        } catch (Exception ex) {
            SaveStatusText.Text = $"Error saving: {ex.Message}";
        }
    }

    /// <summary>Runs the same discovery+classification a real scan would (ScopeContainerScanner.Discover +
    /// ScopeClassifier.ClassifyWithReason) against the in-memory, possibly-unsaved rules - so the preview always
    /// reflects what Save is about to write, not only what's already on disk.</summary>
    private async void Preview_Click(object sender, RoutedEventArgs e) {
        var paksFolder = _settings.PaksFolder?.Trim();
        if (string.IsNullOrWhiteSpace(paksFolder) || !Directory.Exists(paksFolder)) {
            PreviewCountsText.Text = "Set a valid Paks folder on Hash Game Files first.";
            return;
        }

        var ruleSet = new ScopeRuleSet { Rules = ToScopeRules() };
        SetBusy(true);
        PreviewButton.IsEnabled = false;
        PreviewCountsText.Text = "Scanning...";
        PreviewListView.ItemsSource = null;

        try {
            var (rows, error) = await Task.Run(() => BuildPreviewRows(paksFolder, ruleSet));
            if (error != null) {
                PreviewCountsText.Text = error;
            } else {
                _allPreviewRows = rows!;
                ApplyPreviewFilter();
            }
        } finally {
            PreviewButton.IsEnabled = true;
            SetBusy(false);
        }
    }

    private static (List<PreviewRow>? Rows, string? Error) BuildPreviewRows(string paksFolder, ScopeRuleSet ruleSet) {
        try {
            var (containers, diagnostics) = ScopeContainerScanner.Discover(Path.GetFullPath(paksFolder));
            var rows = new List<PreviewRow>();
            foreach (var container in containers) {
                var reason = ScopeClassifier.ClassifyWithReason(container.RootRelativePath, ruleSet);
                rows.Add(new PreviewRow(reason.Action.ToString(), container.RootRelativePath, reason.Explanation));
            }
            // Diagnostics cover both per-folder skips (inaccessible/junction/incomplete IoStore pair) and the
            // one summary line for unsupported file extensions - each surfaced as its own preview row so an
            // ignored/unreadable folder is visible here even though its contents weren't separately walked.
            foreach (var diagnostic in diagnostics)
                rows.Add(new PreviewRow("Unsupported/Unreadable", string.IsNullOrEmpty(diagnostic.Path) ? "(multiple files)" : diagnostic.Path, diagnostic.Message));
            return (rows, null);
        } catch (Exception ex) {
            return (null, $"Scan failed: {ex.Message}");
        }
    }

    private void PreviewFilter_Changed(object sender, RoutedEventArgs e) => ApplyPreviewFilter();

    /// <summary>Game/Mod/Ignore/Unsupported, in the same reading order the counts line already lists them in -
    /// not alphabetical (which would put "Ignore" ahead of "Mod"), so the grouped list and the summary line
    /// agree with each other.</summary>
    private static int GroupRank(string group) => group switch {
        "Game" => 0,
        "Mod" => 1,
        "Ignore" => 2,
        _ => 3, // "Unsupported/Unreadable"
    };

    private void ApplyPreviewFilter() {
        var search = PreviewSearchBox.Text.Trim();
        var groupFilter = PreviewGroupFilterCombo.SelectedItem as string ?? "All";

        var filtered = _allPreviewRows
            .Where(r => (groupFilter == "All" || r.Group == groupFilter) &&
                        (string.IsNullOrEmpty(search) || r.Path.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => GroupRank(r.Group)).ThenBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A plain List<T> ItemsSource can't be grouped - ListCollectionView adds that on top without
        // changing what's actually shown, so "Preview scan scope"'s Game/Mods/Ignored/Unsupported groups
        // (the UI-10 spec's own wording) are visually clustered with their own header/count instead of one
        // long flat list a user has to scan through to tell where one group ends and the next begins.
        var view = new ListCollectionView(filtered);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PreviewRow.Group)));
        PreviewListView.ItemsSource = view;

        var game = _allPreviewRows.Count(r => r.Group == "Game");
        var mod = _allPreviewRows.Count(r => r.Group == "Mod");
        var ignore = _allPreviewRows.Count(r => r.Group == "Ignore");
        var unsupported = _allPreviewRows.Count(r => r.Group == "Unsupported/Unreadable");
        PreviewCountsText.Text = _allPreviewRows.Count == 0
            ? "No preview yet - click \"Preview scan scope\"."
            : $"Game: {game}, Mods: {mod}, Ignored: {ignore}, Unsupported/unreadable: {unsupported}. Showing {filtered.Count} of {_allPreviewRows.Count}.";
    }

    /// <summary>Called by MainWindow whenever some OTHER tab starts/stops a real hashing run - a run already in
    /// progress against the same Paks/Mods folders must not have its scope rules edited out from under it.</summary>
    public void SetEditingLocked(bool locked) {
        _externallyLocked = locked;
        UpdateEditingEnabled();
    }

    private void UpdateEditingEnabled() {
        var enabled = !IsBusy && !_externallyLocked;
        FileFieldsGrid.IsEnabled = enabled;
        RulesEditorGrid.IsEnabled = enabled;
        PreviewButton.IsEnabled = enabled;
    }
}

/// <summary>Display-only row for <see cref="ScopeRulesView"/>'s rules list - deliberately mutable (unlike
/// Core's own immutable <see cref="ScopeRule"/>) since its Action is edited in place via a bound combo box;
/// converted back to real ScopeRule instances only at Validate/Save/Preview time (see ScopeRulesView.ToScopeRules).</summary>
public class ScopeRuleRow {
    public required string Path { get; set; }
    public required bool IsFolder { get; set; }
    public required ScopeAction Action { get; set; }
    public string TypeLabel => IsFolder ? "Folder" : "File";
}

/// <summary>One row of a scan-scope preview - either a real classified container or an unsupported/unreadable
/// diagnostic, both shown in the same list, distinguished by <see cref="Group"/>.</summary>
public record PreviewRow(string Group, string Path, string Reason);
