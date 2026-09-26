using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DekUnrealGameAudit.Core;
using Microsoft.Win32;

namespace DekUnrealGameAudit.Gui.Views;

public partial class HashGameFilesView : UserControl, ISharedFieldView {
    private AppSettings _settings = null!;
    private CancellationTokenSource? _cts;
    /// <summary>Set only when hashing succeeded but writing the result failed - keeps the (potentially
    /// expensive to recompute) manifest available for Retry Save instead of forcing a full re-hash just to
    /// retry what might be a transient or now-fixed output-path problem.</summary>
    private GameManifest? _pendingManifest;
    private string? _lastWrittenPath;

    /// <summary>Raised whenever Paks folder, UE version, AES key, or scope rules path change here - each is also
    /// shown on at least one other tab (Quick Actions shows all four; Hash Mod Files shares UE version/AES
    /// key/scope rules) - see ISharedFieldView for how MainWindow keeps them mirrored live as you type, rather
    /// than only at Capture time, where whichever tab's CaptureToSettings ran last would silently overwrite the
    /// other's edit with its own (stale, never-refreshed) copy.</summary>
    public event EventHandler<SharedFieldChangedEventArgs>? SharedFieldChanged;

    /// <summary>Raised when "Use as Old"/"Use as New" is clicked, with the just-written manifest's path - an
    /// explicit action rather than an automatic handoff (unlike Hash Mod Files' ManifestProduced), since
    /// jumping straight to Compare Game Files and overwriting a field there is a bigger effect than the
    /// mods-manifest auto-fill and should be something the user chose, not something that just happens.</summary>
    public event EventHandler<string>? UseAsOldRequested;
    public event EventHandler<string>? UseAsNewRequested;

    /// <summary>True while a Run is in progress - MainWindow disables profile switching/save/delete while any
    /// tab reports busy, since applying a different profile's fields out from under an in-flight scan (or
    /// deleting the profile that scan is about to save under) could publish a result under settings that no
    /// longer match what's actually running.</summary>
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    private void SetBusy(bool busy) {
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public HashGameFilesView() {
        InitializeComponent();
        EGameComboBox.Attach(UeVersionCombo);
        PaksFolderBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.PaksFolder, PaksFolderBox.Text.Trim()));
            PaksFolderBox.SetFieldValid(SharedFieldValidation.IsExistingFolderOrBlank(PaksFolderBox.Text));
        };
        UeVersionCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.UeVersion, UeVersionCombo.Text.Trim()));
            UeVersionCombo.SetFieldValid(SharedFieldValidation.IsKnownUeVersionOrBlank(UeVersionCombo.Text));
        }));
        AesKeyBox.TextChanged += (_, _) => SharedFieldChanged?.Invoke(this, new(SharedField.AesKey, AesKeyBox.Text.Trim()));
        ScopeRulesBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.ScopeRulesPath, ScopeRulesBox.Text.Trim()));
            ScopeRulesBox.SetFieldValid(SharedFieldValidation.IsLoadableScopeRulesPathOrBlank(ScopeRulesBox.Text));
        };
    }

    /// <summary>Called once by MainWindow right after construction, with the app's single shared settings
    /// instance (not each view loading/owning its own copy - see AppSettings' own doc comment for why).</summary>
    public void Initialize(AppSettings settings) {
        _settings = settings;
        ApplyFromSettings();
    }

    public void ApplyFromSettings() {
        PaksFolderBox.Text = _settings.PaksFolder ?? "";
        UeVersionCombo.Text = _settings.UeVersion ?? "";
        AesKeyBox.Text = _settings.AesKey ?? "";
        VersionTagBox.Text = _settings.VersionTag ?? "";
        OutputPathBox.Text = _settings.GameOutputPath ?? "";
        ScopeRulesBox.Text = _settings.ScopeRulesPath ?? "";
    }

    public void CaptureToSettings() {
        _settings.PaksFolder = PaksFolderBox.Text.Trim();
        _settings.UeVersion = UeVersionCombo.Text.Trim();
        _settings.AesKey = AesKeyBox.Text.Trim();
        _settings.VersionTag = VersionTagBox.Text.Trim();
        _settings.GameOutputPath = OutputPathBox.Text.Trim();
        _settings.ScopeRulesPath = ScopeRulesBox.Text.Trim();
    }

    /// <summary>Applies a shared field pushed from another view (see ISharedFieldView) without touching this
    /// view's own distinct fields (VersionTagBox/OutputPathBox aren't shown anywhere else, so a full
    /// ApplyFromSettings() here would risk clobbering an unsaved edit to one of those).</summary>
    public void SetSharedField(string fieldName, string value) {
        switch (fieldName) {
            case SharedField.PaksFolder: PaksFolderBox.Text = value; break;
            case SharedField.UeVersion: UeVersionCombo.Text = value; break;
            case SharedField.AesKey: AesKeyBox.Text = value; break;
            case SharedField.ScopeRulesPath: ScopeRulesBox.Text = value; break;
        }
    }

    private void BrowsePaksFolder_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFolderDialog { Title = "Select the game's Paks folder" };
        if (dialog.ShowDialog() == true)
            PaksFolderBox.Text = dialog.FolderName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e) {
        var dialog = new SaveFileDialog { Title = "Save manifest as", Filter = "JSON files (*.json)|*.json", FileName = "game-manifest.json" };
        if (dialog.ShowDialog() == true)
            OutputPathBox.Text = dialog.FileName;
    }

    private void BrowseScopeRules_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog { Title = "Select a scope rules JSON file", Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
            ScopeRulesBox.Text = dialog.FileName;
    }

    private async void Run_Click(object sender, RoutedEventArgs e) {
        var paksFolder = PaksFolderBox.Text.Trim();
        var ueVersion = UeVersionCombo.Text.Trim();

        if (string.IsNullOrWhiteSpace(paksFolder) || string.IsNullOrWhiteSpace(ueVersion)) {
            const string message = "Paks folder and UE version are required.";
            AppendLog(message);
            AccessibilityAnnouncer.Announce(ProgressText, message);
            return;
        }

        ScopeRuleSet? scopeRules;
        try {
            scopeRules = ScopeRulesLoader.Load(ScopeRulesBox.Text.Trim());
        } catch (Exception ex) {
            AppendLog($"Scope rules file is invalid: {ex.Message}");
            return;
        }

        // Claims the explicit output path (if any) across all six tabs, not just this one - an
        // auto-named output isn't claimed since AutoFileNaming.AllocateUniquePath already picks a
        // fresh, collision-free name for it.
        if (OperationUiHelpers.TryClaimOutput(OutputPathBox.Text.Trim(), AppendLog) is not { } outputClaim)
            return;

        SetBusy(true);
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        RetrySaveButton.Visibility = Visibility.Collapsed;
        UseAsPanel.Visibility = Visibility.Collapsed;
        FieldsGrid.IsEnabled = false;
        LogBox.Clear();
        ProgressBar.Value = 0;
        AccessibilityAnnouncer.Announce(ProgressText, OperationStatusText.For(OperationStatus.Running));
        _pendingManifest = null;
        _lastWrittenPath = null;

        CaptureToSettings();
        _settings.Save();

        var stopwatch = Stopwatch.StartNew();
        IProgress<string> logProgress = new Progress<string>(AppendLog);
        // Hashing runs each asset on its own thread via Parallel.ForEach, incrementing a shared counter -
        // the callback for a smaller Done value can still be dispatched to this (UI-thread) delegate after
        // one for a larger value, since the increment and the callback aren't atomic together. Tracking the
        // highest Done actually shown and ignoring anything smaller keeps the bar/text monotonically
        // increasing instead of visibly flickering backwards.
        var highestDoneShown = 0;
        IProgress<(int Done, int Total)> scanProgress = new Progress<(int Done, int Total)>(p => {
            if (p.Done < highestDoneShown)
                return;
            highestDoneShown = p.Done;
            ProgressBar.Maximum = p.Total;
            ProgressBar.Value = p.Done;
            AccessibilityAnnouncer.UpdateSilently(ProgressText, p.Total > 0
                ? $"{p.Done:N0} / {p.Total:N0} ({p.Done * 100.0 / p.Total:F0}%) - {OperationUiHelpers.FormatElapsed(stopwatch.Elapsed)} elapsed"
                : "");
        });
        // Coalesces by elapsed time rather than every-Nth-asset - a fixed asset-count interval reports far
        // too often on a huge fast SSD scan and not often enough on a small one, whereas ~10 updates/second
        // reads smoothly regardless of collection size or hashing speed. Interlocked, not locked, since a
        // slightly-early or slightly-late report from a race between hashing threads is harmless - this is
        // a display throttle, not a correctness boundary.
        var lastProgressReportMs = -1000L;

        _cts = new CancellationTokenSource();
        GameManifest? manifest = null;

        // Computing (potentially many minutes for a large Paks folder) and saving are deliberately two
        // separate try/catch blocks - a failure writing the result must not be reported (or retried) the
        // same way as a failure during the scan itself, and shouldn't throw away a manifest that was
        // actually hashed successfully.
        try {
            var options = new ScanOptions {
                TargetFolder = Path.GetFullPath(paksFolder),
                UeVersion = ueVersion,
                AesKeys = AesKeyParsing.ParseAesKeysMultiline(_settings.AesKey),
                ScopeRules = scopeRules,
            };

            var versionTagOverride = VersionTagBox.Text.Trim();
            var token = _cts.Token;

            manifest = await Task.Run(() => GameHasher.Hash(
                options,
                onLog: line => logProgress.Report(line),
                onProgress: (done, total) => {
                    // Reporting every single asset would flood the UI thread across ~1M+ calls.
                    var nowMs = stopwatch.ElapsedMilliseconds;
                    if (done == total || nowMs - Interlocked.Read(ref lastProgressReportMs) >= 100) {
                        Interlocked.Exchange(ref lastProgressReportMs, nowMs);
                        scanProgress.Report((done, total));
                    }
                },
                cancellationToken: token));

            manifest.VersionTag = !string.IsNullOrWhiteSpace(versionTagOverride) ? versionTagOverride : manifest.DetectedGameVersion;
            if (manifest.DetectedGameVersion != null)
                AppendLog($"Detected game version: {manifest.DetectedGameVersion}");
        } catch (OperationCanceledException) {
            AppendLog("Cancelled - no manifest was written.");
            AccessibilityAnnouncer.Announce(ProgressText, OperationStatusText.For(OperationStatus.Cancelled));
        } catch (Exception ex) {
            AppendLog($"Error: {ex.Message}");
            AccessibilityAnnouncer.Announce(ProgressText, OperationStatusText.For(OperationStatus.Failed));
        } finally {
            _cts.Dispose();
            _cts = null;
            CancelButton.IsEnabled = false;
        }

        if (manifest != null)
            await TrySaveManifest(manifest, OutputPathBox.Text.Trim(), stopwatch);

        RunButton.IsEnabled = true;
        FieldsGrid.IsEnabled = true;
        outputClaim.Dispose();
        SetBusy(false);
    }

    private async Task TrySaveManifest(GameManifest manifest, string outPathInput, Stopwatch stopwatch) {
        AccessibilityAnnouncer.Announce(ProgressText, OperationStatusText.For(OperationStatus.Saving));
        try {
            // Serializing/writing a large manifest is itself slow enough to freeze the UI if done
            // directly here.
            var finalOutPath = await Task.Run(() => {
                var path = !string.IsNullOrWhiteSpace(outPathInput)
                    ? outPathInput
                    : AutoFileNaming.AllocateUniquePath(() => GameHasher.BuildAutoFileName(manifest));
                JsonUtil.WriteFile(path, manifest);
                return path;
            });
            AppendLog($"Wrote manifest with {manifest.Assets.Count} assets to {Path.GetFullPath(finalOutPath)} in {OperationUiHelpers.FormatElapsed(stopwatch.Elapsed)}" +
                (manifest.Errors.Count > 0 ? $" ({manifest.Errors.Count} asset(s) failed to hash)" : ""));
            AccessibilityAnnouncer.Announce(ProgressText, manifest.Errors.Count > 0
                ? OperationStatusText.For(OperationStatus.Partial, $"{manifest.Errors.Count} asset(s) failed to hash")
                : OperationStatusText.For(OperationStatus.Completed));
            _pendingManifest = null;
            RetrySaveButton.Visibility = Visibility.Collapsed;
            _lastWrittenPath = Path.GetFullPath(finalOutPath);
            UseAsPanel.Visibility = Visibility.Visible;
        } catch (Exception ex) {
            AppendLog($"Error saving manifest: {ex.Message}");
            AccessibilityAnnouncer.Announce(ProgressText, OperationStatusText.For(OperationStatus.FailedToSave));
            _pendingManifest = manifest;
            RetrySaveButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>See the UseAsOldRequested/UseAsNewRequested doc comment for why this is explicit rather than
    /// an automatic handoff like Hash Mod Files'.</summary>
    private void UseAsOld_Click(object sender, RoutedEventArgs e) {
        if (_lastWrittenPath != null)
            UseAsOldRequested?.Invoke(this, _lastWrittenPath);
    }

    private void UseAsNew_Click(object sender, RoutedEventArgs e) {
        if (_lastWrittenPath != null)
            UseAsNewRequested?.Invoke(this, _lastWrittenPath);
    }

    /// <summary>Re-attempts writing the manifest from the last failed save, using whatever output path is
    /// currently in the box (the user may have just fixed a bad path) - without redoing the scan itself.</summary>
    private async void RetrySave_Click(object sender, RoutedEventArgs e) {
        if (_pendingManifest == null)
            return;

        var manifest = _pendingManifest;
        var outPathInput = OutputPathBox.Text.Trim();

        if (OperationUiHelpers.TryClaimOutput(outPathInput, AppendLog) is not { } outputClaim)
            return;

        SetBusy(true);
        RunButton.IsEnabled = false;
        RetrySaveButton.IsEnabled = false;
        FieldsGrid.IsEnabled = false;
        try {
            await TrySaveManifest(manifest, outPathInput, Stopwatch.StartNew());
        } finally {
            RunButton.IsEnabled = true;
            RetrySaveButton.IsEnabled = true;
            FieldsGrid.IsEnabled = true;
            outputClaim.Dispose();
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) {
        CancelButton.IsEnabled = false;
        _cts?.Cancel();
    }

    /// <summary>The log is a real read-only TextBox, so click-drag-select+Ctrl+C already works - this is
    /// just a one-click "grab all of it" shortcut for pasting a whole run's output into a bug report or chat.</summary>
    private void CopyLog_Click(object sender, RoutedEventArgs e) => LogViewHelper.CopyAll(LogBox);

    private void AppendLog(string line) => LogViewHelper.Append(LogBox, LogScrollViewer, line);
}
