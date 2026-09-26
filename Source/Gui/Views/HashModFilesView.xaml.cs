using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DekUnrealGameAudit.Core;
using Microsoft.Win32;

namespace DekUnrealGameAudit.Gui.Views;

public partial class HashModFilesView : UserControl, ISharedFieldView {
    private AppSettings _settings = null!;
    private CancellationTokenSource? _cts;
    /// <summary>Set only when hashing succeeded but writing the result failed - see HashGameFilesView's
    /// copy of this field for why.</summary>
    private ModManifest? _pendingManifest;

    /// <summary>Raised whenever Mods folder, UE version, AES key, or scope rules path change here - see
    /// HashGameFilesView's copy of this event for why views need to stay in sync rather than each keeping an
    /// independent copy.</summary>
    public event EventHandler<SharedFieldChangedEventArgs>? SharedFieldChanged;

    /// <summary>Raised with the written mods-manifest.json's path after a successful run, so MainWindow can
    /// auto-fill Compare Mod Files' "Mods manifest" field with it - the natural next step after hashing your mods.</summary>
    public event EventHandler<string>? ManifestProduced;

    /// <summary>True while a Run is in progress - see HashGameFilesView.IsBusy for why MainWindow uses this.</summary>
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    private void SetBusy(bool busy) {
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public HashModFilesView() {
        InitializeComponent();
        EGameComboBox.Attach(UeVersionCombo);
        ModsFolderBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.ModsFolder, ModsFolderBox.Text.Trim()));
            ModsFolderBox.SetFieldValid(SharedFieldValidation.IsExistingFolderOrBlank(ModsFolderBox.Text));
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

    public void Initialize(AppSettings settings) {
        _settings = settings;
        ApplyFromSettings();
    }

    public void ApplyFromSettings() {
        ModsFolderBox.Text = _settings.ModsFolder ?? "";
        UeVersionCombo.Text = _settings.UeVersion ?? "";
        AesKeyBox.Text = _settings.AesKey ?? "";
        OutputPathBox.Text = _settings.ModsOutputPath ?? "";
        ScopeRulesBox.Text = _settings.ScopeRulesPath ?? "";
    }

    public void CaptureToSettings() {
        _settings.ModsFolder = ModsFolderBox.Text.Trim();
        _settings.UeVersion = UeVersionCombo.Text.Trim();
        _settings.AesKey = AesKeyBox.Text.Trim();
        _settings.ModsOutputPath = OutputPathBox.Text.Trim();
        _settings.ScopeRulesPath = ScopeRulesBox.Text.Trim();
    }

    /// <summary>Applies a shared field pushed from another view (see ISharedFieldView) without touching this
    /// view's own distinct fields (a full ApplyFromSettings() here would clobber e.g. an unsaved Output file edit).</summary>
    public void SetSharedField(string fieldName, string value) {
        switch (fieldName) {
            case SharedField.ModsFolder: ModsFolderBox.Text = value; break;
            case SharedField.UeVersion: UeVersionCombo.Text = value; break;
            case SharedField.AesKey: AesKeyBox.Text = value; break;
            case SharedField.ScopeRulesPath: ScopeRulesBox.Text = value; break;
        }
    }

    /// <summary>Appends the picked folder as a new line rather than replacing the box's content, so Browse can
    /// be used repeatedly to add several mod roots (CODE-17) instead of only ever setting a single one.</summary>
    private void BrowseModsFolder_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFolderDialog { Title = "Select a mods folder to add" };
        if (dialog.ShowDialog() != true)
            return;
        var existing = ModsFolderBox.Text.TrimEnd();
        ModsFolderBox.Text = string.IsNullOrWhiteSpace(existing) ? dialog.FolderName : existing + Environment.NewLine + dialog.FolderName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e) {
        var dialog = new SaveFileDialog { Title = "Save mods manifest as", Filter = "JSON files (*.json)|*.json", FileName = "mods-manifest.json" };
        if (dialog.ShowDialog() == true)
            OutputPathBox.Text = dialog.FileName;
    }

    private void BrowseScopeRules_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog { Title = "Select a scope rules JSON file", Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
            ScopeRulesBox.Text = dialog.FileName;
    }

    private async void Run_Click(object sender, RoutedEventArgs e) {
        var modsFolder = ModsFolderBox.Text.Trim();
        var ueVersion = UeVersionCombo.Text.Trim();

        if (string.IsNullOrWhiteSpace(modsFolder) || string.IsNullOrWhiteSpace(ueVersion)) {
            const string message = "Mods folder and UE version are required.";
            AppendLog(message);
            ProgressText.Text = message;
            return;
        }

        ScopeRuleSet? scopeRules;
        try {
            scopeRules = ScopeRulesLoader.Load(ScopeRulesBox.Text.Trim());
        } catch (Exception ex) {
            AppendLog($"Scope rules file is invalid: {ex.Message}");
            return;
        }

        // See HashGameFilesView.Run_Click for why only an explicit output path is claimed.
        if (OperationUiHelpers.TryClaimOutput(OutputPathBox.Text.Trim(), AppendLog) is not { } outputClaim)
            return;

        SetBusy(true);
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        RetrySaveButton.Visibility = Visibility.Collapsed;
        FieldsGrid.IsEnabled = false;
        LogBox.Clear();
        ProgressText.Text = OperationStatusText.For(OperationStatus.Running);
        ProgressBar.Value = 0;
        // Only inspects file paths, not content, so there's no meaningful per-asset percentage to report -
        // an indeterminate (animated, no fixed value) bar is a more honest "working..." indicator for the
        // pak-mounting step than one trying to map that onto a 0-100% fill.
        ProgressBar.IsIndeterminate = true;
        _pendingManifest = null;
        _cts = new CancellationTokenSource();

        CaptureToSettings();
        _settings.Save();

        var stopwatch = Stopwatch.StartNew();
        IProgress<string> logProgress = new Progress<string>(AppendLog);

        // Computing and saving are deliberately separate try/catch blocks - see HashGameFilesView.Run_Click.
        ModManifest? manifest = null;
        try {
            var token = _cts.Token;
            var options = new ScanOptions {
                // Raw text, unresolved - ModsHasher/ModRootParsing resolves each line (one-or-more mod roots)
                // to a full path.
                TargetFolder = modsFolder,
                UeVersion = ueVersion,
                AesKeys = AesKeyParsing.ParseAesKeysMultiline(_settings.AesKey),
                ScopeRules = scopeRules,
            };

            manifest = await Task.Run(() => ModsHasher.Hash(options,
                onLog: line => logProgress.Report(line), cancellationToken: token), token);
        } catch (OperationCanceledException) {
            AppendLog("Cancelled - no manifest was written.");
            ProgressText.Text = OperationStatusText.For(OperationStatus.Cancelled);
        } catch (Exception ex) {
            AppendLog($"Error: {ex.Message}");
            ProgressText.Text = OperationStatusText.For(OperationStatus.Failed);
        } finally {
            ProgressBar.IsIndeterminate = false;
        }

        if (manifest != null && !_cts.IsCancellationRequested)
            await TrySaveManifest(manifest, OutputPathBox.Text.Trim(), stopwatch);

        RunButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
        FieldsGrid.IsEnabled = true;
        _cts.Dispose();
        _cts = null;
        outputClaim.Dispose();
        SetBusy(false);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) {
        CancelButton.IsEnabled = false;
        _cts?.Cancel();
    }

    private async Task TrySaveManifest(ModManifest manifest, string outPathInput, Stopwatch stopwatch) {
        ProgressText.Text = OperationStatusText.For(OperationStatus.Saving);
        try {
            var finalOutPath = await Task.Run(() => {
                var path = !string.IsNullOrWhiteSpace(outPathInput)
                    ? outPathInput
                    : AutoFileNaming.AllocateUniquePath(ModsHasher.BuildAutoFileName);
                JsonUtil.WriteFile(path, manifest);
                return path;
            });

            ProgressBar.Maximum = 1;
            ProgressBar.Value = 1;
            ProgressText.Text = $"{OperationStatusText.For(OperationStatus.Completed)} in {OperationUiHelpers.FormatElapsed(stopwatch.Elapsed)}";
            AppendLog($"Wrote manifest with {manifest.Mods.Count} mod containers to {Path.GetFullPath(finalOutPath)}");
            ManifestProduced?.Invoke(this, finalOutPath);
            _pendingManifest = null;
            RetrySaveButton.Visibility = Visibility.Collapsed;
        } catch (Exception ex) {
            AppendLog($"Error saving manifest: {ex.Message}");
            ProgressText.Text = OperationStatusText.For(OperationStatus.FailedToSave);
            _pendingManifest = manifest;
            RetrySaveButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Re-attempts writing the manifest from the last failed save without redoing the scan - see
    /// HashGameFilesView.RetrySave_Click for why.</summary>
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

    /// <summary>See HashGameFilesView.CopyLog_Click for why this exists alongside normal text selection.</summary>
    private void CopyLog_Click(object sender, RoutedEventArgs e) => LogViewHelper.CopyAll(LogBox);

    private void AppendLog(string line) => LogViewHelper.Append(LogBox, LogScrollViewer, line);
}
