using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace DekUnrealGameAudit.Gui;

public partial class MainWindow : Window {
    private readonly AppSettings _settings;
    private readonly ProfileStore _profiles;
    private readonly WindowGeometry _windowGeometry;
    private readonly ISharedFieldView[] _sharedFieldViews;
    private UIElement? _activeTab;
    private bool _syncingSharedFields;
    private bool _closeWarningShowing;
    private bool _profileTrackingReady;
    private bool _changingProfileSelection;
    private bool _aesOmissionWarned;
    private string? _activeProfileName;
    private AppSettings? _activeProfileSnapshot;

    public MainWindow() {
        InitializeComponent();

        // Restores the last size you left the window at, if any - falls back to the XAML Width/Height
        // default (itself equal to MinWidth/MinHeight) on first run. Never smaller than MinWidth/MinHeight,
        // in case a saved value predates a later increase to the minimum.
        _windowGeometry = WindowGeometry.Load();
        if (_windowGeometry.Width is { } savedWidth && savedWidth >= MinWidth)
            Width = savedWidth;
        if (_windowGeometry.Height is { } savedHeight && savedHeight >= MinHeight)
            Height = savedHeight;

        // One shared settings instance for all six tabs, rather than each tab loading its own
        // independent copy - otherwise switching tabs would silently lose whatever you'd typed
        // (each tab's own copy wouldn't know about edits made on another tab).
        _settings = AppSettings.Load();
        _settings.AesKeyOmittedOnSave += Settings_AesKeyOmittedOnSave;
        _profiles = ProfileStore.Load();
        HashGameFilesTab.Initialize(_settings);
        CompareGameFilesTab.Initialize(_settings);
        HashModFilesTab.Initialize(_settings);
        CompareModFilesTab.Initialize(_settings);
        HashHeaderFilesTab.Initialize(_settings);
        CompareHeaderFilesTab.Initialize(_settings);
        QuickActionsTab.Initialize(_settings);
        ScopeRulesTab.Initialize(_settings);

        // Paks folder/Mods folder/UE version/AES key/scope rules path/header folder each appear on more than
        // one tab (Quick Actions shows all six; Hash Game/Mod Files share UE version+AES key+scope rules path
        // between themselves) - keep every copy mirrored live as you type, rather than only at Capture time,
        // where whichever tab's CaptureToSettings ran last would silently overwrite another's edit with its
        // own (stale, never-refreshed) copy. One relay handles every field/view combination uniformly instead
        // of a bespoke sync method per field-group - see ISharedFieldView/OnSharedFieldChanged.
        _sharedFieldViews = [HashGameFilesTab, HashModFilesTab, HashHeaderFilesTab, ScopeRulesTab, QuickActionsTab];
        foreach (var view in _sharedFieldViews)
            view.SharedFieldChanged += OnSharedFieldChanged;

        // A completed run hands its output straight to whichever screen naturally consumes it next,
        // instead of making you re-browse for the file you just created.
        HashModFilesTab.ManifestProduced += (_, path) => CompareModFilesTab.SetModsManifestPath(path);
        CompareGameFilesTab.CompareProduced += (_, path) => CompareModFilesTab.SetGameComparePath(path);

        // "Use as Old"/"Use as New" are explicit clicks (unlike the automatic handoffs above - see
        // HashGameFilesView.UseAsOldRequested for why), so also jump straight to the screen being filled in,
        // matching what the user just asked to do.
        HashGameFilesTab.UseAsOldRequested += (_, path) => { CompareGameFilesTab.SetOldManifestPath(path); NavCompareGameFiles.IsChecked = true; };
        HashGameFilesTab.UseAsNewRequested += (_, path) => { CompareGameFilesTab.SetNewManifestPath(path); NavCompareGameFiles.IsChecked = true; };
        HashHeaderFilesTab.UseAsOldRequested += (_, path) => { CompareHeaderFilesTab.SetOldManifestPath(path); NavCompareHeaderFiles.IsChecked = true; };
        HashHeaderFilesTab.UseAsNewRequested += (_, path) => { CompareHeaderFilesTab.SetNewManifestPath(path); NavCompareHeaderFiles.IsChecked = true; };

        // Disable profile switching/save/delete while any tab has a run in progress - applying a different
        // profile's fields out from under an in-flight scan (or deleting the very profile it's about to save
        // under) could publish a result under settings that no longer match what's actually running. Full
        // per-operation task tracking is a larger, separate piece of work; this is the narrower, immediately
        // actionable slice of it.
        HashGameFilesTab.BusyChanged += (_, _) => UpdateProfileControlsEnabled();
        CompareGameFilesTab.BusyChanged += (_, _) => UpdateProfileControlsEnabled();
        HashModFilesTab.BusyChanged += (_, _) => UpdateProfileControlsEnabled();
        CompareModFilesTab.BusyChanged += (_, _) => UpdateProfileControlsEnabled();
        HashHeaderFilesTab.BusyChanged += (_, _) => UpdateProfileControlsEnabled();
        CompareHeaderFilesTab.BusyChanged += (_, _) => UpdateProfileControlsEnabled();
        QuickActionsTab.BusyChanged += (_, _) => UpdateProfileControlsEnabled();
        ScopeRulesTab.BusyChanged += (_, _) => UpdateProfileControlsEnabled();

        // Scope Rules must not be edited while a real hashing run elsewhere is reading the file it points at -
        // "prevent edits during active work" (UI-10). Deliberately excludes ScopeRulesTab's own IsBusy (its
        // own Preview scan already disables its own controls directly in ScopeRulesView.Preview_Click).
        HashGameFilesTab.BusyChanged += (_, _) => UpdateScopeRulesLock();
        CompareGameFilesTab.BusyChanged += (_, _) => UpdateScopeRulesLock();
        HashModFilesTab.BusyChanged += (_, _) => UpdateScopeRulesLock();
        CompareModFilesTab.BusyChanged += (_, _) => UpdateScopeRulesLock();
        HashHeaderFilesTab.BusyChanged += (_, _) => UpdateScopeRulesLock();
        CompareHeaderFilesTab.BusyChanged += (_, _) => UpdateScopeRulesLock();
        QuickActionsTab.BusyChanged += (_, _) => UpdateScopeRulesLock();

        RefreshProfileList();

        // Set after every tab is wired up (not via IsChecked="True" in XAML) so the resulting
        // Checked event - which reads the x:Name fields above - never fires before they exist.
        // Guide (the app logo) is the default landing view, not the first action tab.
        NavGuide.IsChecked = true;

        // Text changes from every descendant view bubble to the window, including edits to fields that are
        // not part of the shared-field relay. This keeps the profile's "modified" indicator complete without
        // requiring every view to expose another near-identical event solely for dirty tracking.
        AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => RefreshProfileDirtyStatus()));
        _profileTrackingReady = true;

        Closing += MainWindow_Closing;
    }

    // Pushes a just-typed value from its source view into _settings and every OTHER view in
    // _sharedFieldViews - each view's own SetSharedField silently ignores field names it doesn't display, so
    // broadcasting to all of them (rather than tracking which views own which field) stays simple. The
    // re-entrancy guard stops a target view's own SetSharedField call (setting .Text raises the same changed
    // event regardless of who set it) from bouncing straight back here.
    private void OnSharedFieldChanged(object? sender, SharedFieldChangedEventArgs e) {
        if (_syncingSharedFields)
            return;

        _syncingSharedFields = true;
        try {
            ApplyToSettings(e.FieldName, e.Value);
            foreach (var view in _sharedFieldViews)
                if (!ReferenceEquals(view, sender))
                    view.SetSharedField(e.FieldName, e.Value);
        } finally {
            _syncingSharedFields = false;
        }
        RefreshProfileDirtyStatus();
    }

    private void Settings_AesKeyOmittedOnSave(object? sender, EventArgs e) {
        SetProfileStatus("Warning: AES key could not be encrypted, so it was not saved.", modified: true);
        if (_aesOmissionWarned)
            return;
        _aesOmissionWarned = true;
        MessageBox.Show(
            "Windows could not encrypt the AES key, so the key was not written to disk. Other settings were saved. " +
            "The key remains available only for this running session; fix the Windows profile/DPAPI problem and save again.",
            "AES key not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ApplyToSettings(string fieldName, string value) {
        switch (fieldName) {
            case SharedField.PaksFolder: _settings.PaksFolder = value; break;
            case SharedField.ModsFolder: _settings.ModsFolder = value; break;
            case SharedField.UeVersion: _settings.UeVersion = value; break;
            case SharedField.AesKey: _settings.AesKey = value; break;
            case SharedField.ScopeRulesPath: _settings.ScopeRulesPath = value; break;
            case SharedField.HeaderDumpFolder: _settings.HeaderDumpFolder = value; break;
        }
    }

    // Every section's View stays a single long-lived instance parked in the same Grid cell
    // (rather than NavigationView's usual Frame/TargetPageType navigation, which recreates a
    // fresh page instance on every visit) - so live scan progress and typed-but-unsaved fields
    // survive switching sections, exactly like the old TabControl did.
    private void NavItem_Checked(object sender, RoutedEventArgs e) {
        if (_activeTab != null)
            _activeTab.Visibility = Visibility.Collapsed;

        _activeTab = ((RadioButton)sender).Tag switch {
            "HashGameFiles" => HashGameFilesTab,
            "CompareGameFiles" => CompareGameFilesTab,
            "HashModFiles" => HashModFilesTab,
            "CompareModFiles" => CompareModFilesTab,
            "HashHeaderFiles" => HashHeaderFilesTab,
            "CompareHeaderFiles" => CompareHeaderFilesTab,
            "QuickActions" => QuickActionsTab,
            "ScopeRules" => ScopeRulesTab,
            "Guide" => GuideTab,
            _ => HashGameFilesTab,
        };
        _activeTab.Visibility = Visibility.Visible;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e) {
        // Chosen close policy: block, don't cancel-and-close. A background run's completion handler
        // still expects to touch this window's controls (progress bar, log, settings save) - letting
        // the window close out from under it risks a cross-thread exception or an abandoned write
        // partway through (AtomicFile's temp file wouldn't corrupt the real output, but it also
        // wouldn't finish it). Cancelling first (where a tab has a Cancel button) then closing is the
        // one policy that's always safe.
        if (AnyTabBusy) {
            e.Cancel = true;

            // MessageBox.Show pumps its own nested message loop, which can dispatch another queued
            // WM_CLOSE back into this same handler before the first dialog is dismissed (e.g. Alt+F4
            // held down, or the title bar's X clicked repeatedly) - without this guard, each re-entrant
            // call shows another dialog on top of the last, stacking an unbounded pile of identical
            // warnings that can only be dismissed one at a time.
            if (_closeWarningShowing)
                return;

            _closeWarningShowing = true;
            try {
                MessageBox.Show(
                    "A run is still in progress. Cancel it (or wait for it to finish) before closing.",
                    "DekUnrealGameAudit", MessageBoxButton.OK, MessageBoxImage.Warning);
            } finally {
                _closeWarningShowing = false;
            }
            return;
        }

        // Persist even fields that were typed but never actually Run - each tab's Run button
        // already saves on success, but this catches everything else on the way out.
        CaptureAll();
        _settings.Save();

        // RestoreBounds (not ActualWidth/Height) when maximized, so a maximized close doesn't save the full
        // screen size as next launch's "normal" size - it would just look maximized-but-not-quite forever.
        var (width, height) = WindowState == WindowState.Maximized
            ? (RestoreBounds.Width, RestoreBounds.Height)
            : (ActualWidth, ActualHeight);
        _windowGeometry.Width = width;
        _windowGeometry.Height = height;
        _windowGeometry.Save();
    }

    private void CaptureAll() {
        HashGameFilesTab.CaptureToSettings();
        CompareGameFilesTab.CaptureToSettings();
        HashModFilesTab.CaptureToSettings();
        CompareModFilesTab.CaptureToSettings();
        HashHeaderFilesTab.CaptureToSettings();
        CompareHeaderFilesTab.CaptureToSettings();
        QuickActionsTab.CaptureToSettings();
        ScopeRulesTab.CaptureToSettings();
    }

    private void ApplyAll() {
        HashGameFilesTab.ApplyFromSettings();
        CompareGameFilesTab.ApplyFromSettings();
        HashModFilesTab.ApplyFromSettings();
        CompareModFilesTab.ApplyFromSettings();
        HashHeaderFilesTab.ApplyFromSettings();
        CompareHeaderFilesTab.ApplyFromSettings();
        QuickActionsTab.ApplyFromSettings();
        ScopeRulesTab.ApplyFromSettings();
    }

    private bool AnyTabBusy =>
        HashGameFilesTab.IsBusy || CompareGameFilesTab.IsBusy || HashModFilesTab.IsBusy ||
        CompareModFilesTab.IsBusy || HashHeaderFilesTab.IsBusy || CompareHeaderFilesTab.IsBusy ||
        QuickActionsTab.IsBusy || ScopeRulesTab.IsBusy;

    private void UpdateProfileControlsEnabled() {
        var enabled = !AnyTabBusy;
        ProfileCombo.IsEnabled = enabled;
        SaveProfileButton.IsEnabled = enabled;
        DeleteProfileButton.IsEnabled = enabled;
    }

    private void UpdateScopeRulesLock() {
        var othersBusy = HashGameFilesTab.IsBusy || CompareGameFilesTab.IsBusy || HashModFilesTab.IsBusy ||
            CompareModFilesTab.IsBusy || HashHeaderFilesTab.IsBusy || CompareHeaderFilesTab.IsBusy || QuickActionsTab.IsBusy;
        ScopeRulesTab.SetEditingLocked(othersBusy);
    }

    // Keeps the ComboBox's dropdown list in sync with the store without disturbing whatever the
    // user currently has typed/selected in the editable text part.
    private void RefreshProfileList() {
        var current = ProfileCombo.Text;
        ProfileCombo.ItemsSource = _profiles.Profiles.Keys.OrderBy(n => n).ToList();
        ProfileCombo.Text = current;
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e) {
        var name = ProfileCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) {
            MessageBox.Show("Type a profile name first.", "DekUnrealGameAudit", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_profiles.Profiles.ContainsKey(name) &&
            MessageBox.Show($"Replace the existing profile '{name}' with the current fields?",
                "Replace profile", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        CaptureAll();
        if (!_profiles.SaveProfile(name, _settings)) {
            MessageBox.Show(
                $"Could not save profile '{name}' - the app's data folder may not be writable right now. Nothing was saved; try again.",
                "DekUnrealGameAudit", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        if (_profiles.LastSaveOmittedAesKey) {
            MessageBox.Show(
                $"Profile '{name}' was saved without its AES key because Windows could not encrypt the key. " +
                "The plaintext key was not written to disk.",
                "AES key not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        RefreshProfileList();
        _activeProfileName = name;
        _activeProfileSnapshot = _settings.Snapshot();
        _changingProfileSelection = true;
        try {
            ProfileCombo.SelectedItem = name;
            ProfileCombo.Text = name;
        } finally {
            _changingProfileSelection = false;
        }
        RefreshProfileDirtyStatus();
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e) {
        var name = ProfileCombo.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || !_profiles.Profiles.ContainsKey(name))
            return;

        if (MessageBox.Show($"Delete profile '{name}'?", "DekUnrealGameAudit", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (!_profiles.DeleteProfile(name)) {
            MessageBox.Show(
                $"Could not delete profile '{name}' - the app's data folder may not be writable right now. It has not been deleted.",
                "DekUnrealGameAudit", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        RefreshProfileList();
        ProfileCombo.Text = "";
        if (string.Equals(_activeProfileName, name, StringComparison.OrdinalIgnoreCase)) {
            _activeProfileName = null;
            _activeProfileSnapshot = null;
            RefreshProfileDirtyStatus();
        }
    }

    // Fires only when the selection actually resolves to an existing item in the dropdown (not on
    // every keystroke of a new, not-yet-saved name), so picking a saved profile applies it immediately.
    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (_changingProfileSelection)
            return;
        if (ProfileCombo.SelectedItem is not string name || !_profiles.Profiles.TryGetValue(name, out var profile))
            return;

        if (_activeProfileName != null && !string.Equals(_activeProfileName, name, StringComparison.OrdinalIgnoreCase) &&
            HasUnsavedProfileChanges()) {
            var choice = MessageBox.Show(
                $"Profile '{_activeProfileName}' has unsaved changes. Save them before switching to '{name}'?",
                "Unsaved profile changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Cancel) {
                _changingProfileSelection = true;
                try {
                    ProfileCombo.SelectedItem = _activeProfileName;
                    ProfileCombo.Text = _activeProfileName;
                } finally {
                    _changingProfileSelection = false;
                }
                return;
            }
            if (choice == MessageBoxResult.Yes) {
                CaptureAll();
                if (!_profiles.SaveProfile(_activeProfileName, _settings)) {
                    MessageBox.Show($"Could not save profile '{_activeProfileName}'. The profile switch was cancelled.",
                        "Profile not saved", MessageBoxButton.OK, MessageBoxImage.Error);
                    _changingProfileSelection = true;
                    try {
                        ProfileCombo.SelectedItem = _activeProfileName;
                        ProfileCombo.Text = _activeProfileName;
                    } finally {
                        _changingProfileSelection = false;
                    }
                    return;
                }
                if (_profiles.LastSaveOmittedAesKey)
                    MessageBox.Show($"Profile '{_activeProfileName}' was saved without its AES key because Windows could not encrypt it.",
                        "AES key not saved", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // _settings is fully updated by CopyFrom below *before* any tab's UI is touched, so it's the single
        // authoritative source every ApplyFromSettings() call reads from - suppressing the shared-field sync
        // during ApplyAll() doesn't lose anything, it just stops that sync from reading a field (e.g.
        // HashModFilesTab's AesKeyBox) that ApplyFromSettings hasn't gotten around to updating yet on THIS
        // tab and writing that stale, pre-switch value back into _settings, clobbering the profile's real one.
        // That race is exactly what used to let switching to a profile with a different key silently keep the
        // previous profile's key in memory (and on the next save, on disk).
        _settings.CopyFrom(profile);
        _syncingSharedFields = true;
        try {
            ApplyAll();
        } finally {
            _syncingSharedFields = false;
        }
        _activeProfileName = name;
        _activeProfileSnapshot = _settings.Snapshot();
        RefreshProfileDirtyStatus();
    }

    private bool HasUnsavedProfileChanges() {
        if (_activeProfileSnapshot == null)
            return false;
        CaptureAll();
        return !_settings.ContentEquals(_activeProfileSnapshot);
    }

    private void RefreshProfileDirtyStatus() {
        if (!_profileTrackingReady || _syncingSharedFields)
            return;
        if (_activeProfileName == null || _activeProfileSnapshot == null) {
            SetProfileStatus("No saved profile selected.", modified: false);
            return;
        }
        CaptureAll();
        var modified = !_settings.ContentEquals(_activeProfileSnapshot);
        SetProfileStatus(modified ? $"Profile: {_activeProfileName} (modified)" : $"Profile: {_activeProfileName}", modified);
    }

    /// <summary>Reuses the app's one "pay attention" style (WarningTextStyle/NexusOrangeBrush, already used for
    /// ConflictText/ResultWarningsText) for the "(modified)" state, instead of just changing the words - a
    /// dirty profile is exactly the kind of thing that style exists for.</summary>
    private void SetProfileStatus(string text, bool modified) {
        ProfileStatusText.Text = text;
        if (modified) {
            ProfileStatusText.SetResourceReference(TextBlock.ForegroundProperty, "NexusOrangeBrush");
            ProfileStatusText.Opacity = 1.0;
        } else {
            ProfileStatusText.ClearValue(TextBlock.ForegroundProperty);
            ProfileStatusText.Opacity = 0.75;
        }
    }
}
