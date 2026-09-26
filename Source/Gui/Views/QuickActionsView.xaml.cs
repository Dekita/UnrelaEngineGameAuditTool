using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DekUnrealGameAudit.Core;
using Microsoft.Win32;

namespace DekUnrealGameAudit.Gui.Views;

public partial class QuickActionsView : UserControl, ISharedFieldView {
    private AppSettings _settings = null!;
    private CancellationTokenSource? _cts;
    private string? _lastRunDirectory;

    /// <summary>See HashGameFilesView.IsBusy for why MainWindow uses this.</summary>
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    /// <summary>Raised whenever any of this view's six config fields change - all six are also edited on
    /// another tab. See HashGameFilesView's copy of this event for why views need to stay in sync rather than
    /// each keeping an independent copy.</summary>
    public event EventHandler<SharedFieldChangedEventArgs>? SharedFieldChanged;

    private void SetBusy(bool busy) {
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public QuickActionsView() {
        InitializeComponent();
        PaksFolderBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.PaksFolder, PaksFolderBox.Text.Trim()));
            PaksFolderBox.SetFieldValid(SharedFieldValidation.IsExistingFolderOrBlank(PaksFolderBox.Text));
        };
        ModsFolderBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.ModsFolder, ModsFolderBox.Text.Trim()));
            ModsFolderBox.SetFieldValid(SharedFieldValidation.IsExistingFolderOrBlank(ModsFolderBox.Text));
        };
        EGameComboBox.Attach(UeVersionCombo);
        UeVersionCombo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.UeVersion, UeVersionCombo.Text.Trim()));
            UeVersionCombo.SetFieldValid(SharedFieldValidation.IsKnownUeVersionOrBlank(UeVersionCombo.Text));
        }));
        AesKeyBox.TextChanged += (_, _) => SharedFieldChanged?.Invoke(this, new(SharedField.AesKey, AesKeyBox.Text.Trim()));
        ScopeRulesBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.ScopeRulesPath, ScopeRulesBox.Text.Trim()));
            ScopeRulesBox.SetFieldValid(SharedFieldValidation.IsLoadableScopeRulesPathOrBlank(ScopeRulesBox.Text));
        };
        HeaderDumpFolderBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.HeaderDumpFolder, HeaderDumpFolderBox.Text.Trim()));
            HeaderDumpFolderBox.SetFieldValid(SharedFieldValidation.IsExistingFolderOrBlank(HeaderDumpFolderBox.Text));
            RefreshHeaderFreshnessNote();
        };
        OutputRootBox.TextChanged += (_, _) => RefreshBaselineStatus();
    }

    public void Initialize(AppSettings settings) {
        _settings = settings;
        ApplyFromSettings();
    }

    public void ApplyFromSettings() {
        PaksFolderBox.Text = _settings.PaksFolder ?? "";
        ModsFolderBox.Text = _settings.ModsFolder ?? "";
        UeVersionCombo.Text = _settings.UeVersion ?? "";
        AesKeyBox.Text = _settings.AesKey ?? "";
        ScopeRulesBox.Text = _settings.ScopeRulesPath ?? "";
        HeaderDumpFolderBox.Text = _settings.HeaderDumpFolder ?? "";
        OutputRootBox.Text = _settings.QuickActionsOutputPath ?? "";
        RefreshHeaderFreshnessNote();
        RefreshBaselineStatus();
    }

    public void CaptureToSettings() {
        _settings.PaksFolder = PaksFolderBox.Text.Trim();
        _settings.ModsFolder = ModsFolderBox.Text.Trim();
        _settings.UeVersion = UeVersionCombo.Text.Trim();
        _settings.AesKey = AesKeyBox.Text.Trim();
        _settings.ScopeRulesPath = ScopeRulesBox.Text.Trim();
        _settings.HeaderDumpFolder = HeaderDumpFolderBox.Text.Trim();
        _settings.QuickActionsOutputPath = OutputRootBox.Text.Trim();
    }

    /// <summary>Applies a shared field pushed from another view (see ISharedFieldView) without touching this
    /// view's own distinct Output root field.</summary>
    public void SetSharedField(string fieldName, string value) {
        switch (fieldName) {
            case SharedField.PaksFolder: PaksFolderBox.Text = value; break;
            case SharedField.ModsFolder: ModsFolderBox.Text = value; break;
            case SharedField.UeVersion: UeVersionCombo.Text = value; break;
            case SharedField.AesKey: AesKeyBox.Text = value; break;
            case SharedField.ScopeRulesPath: ScopeRulesBox.Text = value; break;
            case SharedField.HeaderDumpFolder: HeaderDumpFolderBox.Text = value; RefreshHeaderFreshnessNote(); break;
        }
    }

    private void RefreshHeaderFreshnessNote() {
        HeaderFreshnessNote.Visibility = string.IsNullOrWhiteSpace(HeaderDumpFolderBox.Text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void BrowsePaksFolder_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFolderDialog { Title = "Select the game's Paks folder" };
        if (dialog.ShowDialog() == true)
            PaksFolderBox.Text = dialog.FolderName;
    }

    /// <summary>Appends the picked folder as a new line rather than replacing the box's content - see
    /// HashModFilesView.BrowseModsFolder_Click for why (CODE-17 multi-root support).</summary>
    private void BrowseModsFolder_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFolderDialog { Title = "Select a mods folder to add" };
        if (dialog.ShowDialog() != true)
            return;
        var existing = ModsFolderBox.Text.TrimEnd();
        ModsFolderBox.Text = string.IsNullOrWhiteSpace(existing) ? dialog.FolderName : existing + Environment.NewLine + dialog.FolderName;
    }

    private void BrowseScopeRules_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog { Title = "Select a scope rules JSON file", Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
            ScopeRulesBox.Text = dialog.FileName;
    }

    private void BrowseHeaderDumpFolder_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFolderDialog { Title = "Select UE4SS's own output folder (next to UE4SS.dll)" };
        if (dialog.ShowDialog() == true)
            HeaderDumpFolderBox.Text = dialog.FolderName;
    }

    private void RefreshBaselineStatus() {
        var outputRoot = OutputRootBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputRoot)) {
            BaselineStatusText.Text = "Set an output root to check for an existing baseline.";
            AfterUpdateButton.IsEnabled = false;
            return;
        }

        var baseline = TryLoadBaseline(outputRoot);
        if (baseline == null) {
            BaselineStatusText.Text = "No baseline yet - click \"Before Update\" to create one.";
            AfterUpdateButton.IsEnabled = false;
        } else {
            BaselineStatusText.Text = $"Baseline ready: run {baseline.Value.Record.RunId} (captured {baseline.Value.Record.CreatedAt}).";
            AfterUpdateButton.IsEnabled = true;
        }
    }

    /// <summary>Wraps RunDirectoryStore.TryLoadActiveBaseline - a malformed/corrupt active-baseline.json (e.g.
    /// hand-edited) shouldn't crash the summary refresh, just show as "no baseline" until a fresh Before run
    /// replaces it.</summary>
    private static (string RunDirectory, RunRecord Record)? TryLoadBaseline(string outputRoot) {
        try {
            return RunDirectoryStore.TryLoadActiveBaseline(outputRoot);
        } catch {
            return null;
        }
    }

    private void BrowseOutputRoot_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFolderDialog { Title = "Select (or create) the Quick Actions output root" };
        if (dialog.ShowDialog() == true)
            OutputRootBox.Text = dialog.FolderName;
    }

    /// <summary>Builds the shared options both actions run with, validating everything a CLI caller would have
    /// to provide explicitly but this view instead reads from the profile fields already configured on the
    /// other tabs - "after profile setup, each action runs... without manually choosing intermediate manifests."</summary>
    private QuickActionOptions? BuildOptions() {
        var paksFolder = PaksFolderBox.Text.Trim();
        var modsFolder = ModsFolderBox.Text.Trim();
        var ueVersion = UeVersionCombo.Text.Trim();
        var outputRoot = OutputRootBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(paksFolder) || string.IsNullOrWhiteSpace(modsFolder) || string.IsNullOrWhiteSpace(ueVersion)) {
            AppendLog("Paks folder, Mods folder and UE version are required.");
            return null;
        }
        if (string.IsNullOrWhiteSpace(outputRoot)) {
            AppendLog("Output root is required.");
            return null;
        }

        ScopeRuleSet? scopeRules;
        try {
            scopeRules = ScopeRulesLoader.Load(ScopeRulesBox.Text.Trim());
        } catch (Exception ex) {
            AppendLog($"Scope rules file is invalid: {ex.Message}");
            return null;
        }

        var headerFolder = HeaderDumpFolderBox.Text.Trim();
        return new QuickActionOptions {
            PaksFolder = Path.GetFullPath(paksFolder),
            // Raw text, unresolved - one-or-more mod roots (CODE-17), resolved per line by
            // ModsHasher/ModRootParsing.
            ModsFolder = modsFolder,
            UeVersion = ueVersion,
            AesKeys = AesKeyParsing.ParseAesKeysMultiline(AesKeyBox.Text),
            ScopeRules = scopeRules,
            HeaderFolder = string.IsNullOrWhiteSpace(headerFolder) ? null : Path.GetFullPath(headerFolder),
            OutputRoot = Path.GetFullPath(outputRoot),
        };
    }

    private async void BeforeUpdate_Click(object sender, RoutedEventArgs e) {
        var options = BuildOptions();
        if (options == null)
            return;

        BeginRun();
        StatusText.Text = "Capturing baseline...";
        _lastRunDirectory = null;
        ResultsPanel.Visibility = Visibility.Collapsed;

        CaptureToSettings();
        _settings.Save();

        RunRecord? record = null;
        try {
            var token = _cts!.Token;
            record = await Task.Run(() => BeforeUpdateOrchestrator.Run(options, onLog: line => Dispatcher.Invoke(() => AppendLog(line)), cancellationToken: token));
        } catch (OperationCanceledException) {
            AppendLog("Cancelled - the interrupted run was recorded, and no baseline was replaced.");
            StatusText.Text = OperationStatusText.For(OperationStatus.Cancelled);
        } catch (Exception ex) {
            AppendLog($"Error: {ex.Message}");
        }

        if (record != null) {
            _lastRunDirectory = Path.Combine(options.OutputRoot, "runs", record.RunId);
            var headerOk = options.HeaderFolder == null || record.HeaderStage == StageStatus.Completed;
            var activated = record.GameStage == StageStatus.Completed && record.ModsStage == StageStatus.Completed && headerOk;
            StatusText.Text = activated ? "Baseline activated." : "Baseline NOT activated - see the log.";
        } else if (StatusText.Text != OperationStatusText.For(OperationStatus.Cancelled)) {
            StatusText.Text = OperationStatusText.For(OperationStatus.Failed);
        }

        EndRun();
        RefreshBaselineStatus();
    }

    private async void AfterUpdate_Click(object sender, RoutedEventArgs e) {
        var options = BuildOptions();
        if (options == null)
            return;

        BeginRun();
        StatusText.Text = "Auditing update...";
        _lastRunDirectory = null;
        ResultsPanel.Visibility = Visibility.Collapsed;

        CaptureToSettings();
        _settings.Save();

        QuickActionReport? report = null;
        try {
            var token = _cts!.Token;
            report = await Task.Run(() => AfterUpdateOrchestrator.Run(options, onLog: line => Dispatcher.Invoke(() => AppendLog(line)), cancellationToken: token));
        } catch (OperationCanceledException) {
            AppendLog("Cancelled - no combined report was published.");
            StatusText.Text = OperationStatusText.For(OperationStatus.Cancelled);
        } catch (Exception ex) {
            AppendLog($"Error: {ex.Message}");
        }

        if (report != null) {
            _lastRunDirectory = Path.Combine(options.OutputRoot, "runs", report.AfterRunId);
            ShowResults(report);
            StatusText.Text = report.SkippedOrFailedStages.Count > 0
                ? OperationStatusText.For(OperationStatus.Partial, $"{report.SkippedOrFailedStages.Count} stage(s) skipped/failed")
                : OperationStatusText.For(OperationStatus.Completed);
        } else if (StatusText.Text != OperationStatusText.For(OperationStatus.Cancelled)) {
            StatusText.Text = OperationStatusText.For(OperationStatus.Failed);
        }

        EndRun();
    }

    private void ShowResults(QuickActionReport report) {
        ResultGameText.Text = report.GameComparePath != null
            ? $"Game: {report.GameAdded} added, {report.GameRemoved} removed, {report.GameChanged} changed, {report.GameUnchanged} unchanged, {report.GameUncertain} uncertain."
            : "Game: not compared.";
        ResultModsText.Text = report.ModsComparePath != null
            ? $"Mods: {report.ModsNeedingReview} potentially affected (review required), {report.ModsNeedingUncertainReview} need review, {report.ModsUpToDate} unaffected."
            : "Mods: not assessed.";
        ResultHeaderText.Text = report.HeaderComparePath != null
            ? $"Headers: {report.HeaderCxxChangedTypes} CXX type(s) changed, {report.HeaderUhtChangedTypes} UHT type(s) changed."
            : "Headers: not compared.";

        var delta = report.ModInventoryDelta;
        ResultInventoryText.Text = delta.Added.Count > 0 || delta.Removed.Count > 0
            ? $"Mod inventory changed since baseline: {delta.Added.Count} added, {delta.Removed.Count} removed."
            : "Mod inventory unchanged since baseline.";

        ResultWarningsText.Text = report.Warnings.Count > 0 ? string.Join(Environment.NewLine, report.Warnings) : "";
        ResultWarningsText.Visibility = report.Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ResultSkippedText.Text = report.SkippedOrFailedStages.Count > 0
            ? "Skipped/failed: " + string.Join(" ", report.SkippedOrFailedStages)
            : "";
        ResultSkippedText.Visibility = report.SkippedOrFailedStages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        ResultsPanel.Visibility = Visibility.Visible;
    }

    private void BeginRun() {
        SetBusy(true);
        BeforeUpdateButton.IsEnabled = false;
        AfterUpdateButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        FieldsGrid.IsEnabled = false;
        LogBox.Clear();
        ProgressBar.IsIndeterminate = true;
        _cts = new CancellationTokenSource();
    }

    private void EndRun() {
        _cts?.Dispose();
        _cts = null;
        ProgressBar.IsIndeterminate = false;
        BeforeUpdateButton.IsEnabled = true;
        CancelButton.IsEnabled = false;
        FieldsGrid.IsEnabled = true;
        SetBusy(false);
        RefreshBaselineStatus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) {
        CancelButton.IsEnabled = false;
        _cts?.Cancel();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e) => LogViewHelper.CopyAll(LogBox);

    private void OpenReport_Click(object sender, RoutedEventArgs e) {
        if (_lastRunDirectory == null)
            return;
        var reportPath = Path.Combine(_lastRunDirectory, "report.md");
        if (File.Exists(reportPath))
            Process.Start(new ProcessStartInfo(reportPath) { UseShellExecute = true });
        else
            AppendLog($"Report not found at '{reportPath}'.");
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) {
        if (_lastRunDirectory != null && Directory.Exists(_lastRunDirectory))
            Process.Start(new ProcessStartInfo(_lastRunDirectory) { UseShellExecute = true });
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e) {
        if (_lastRunDirectory != null)
            Clipboard.SetText(_lastRunDirectory);
    }

    private void AppendLog(string line) => LogViewHelper.Append(LogBox, LogScrollViewer, line);
}
