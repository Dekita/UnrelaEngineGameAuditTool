using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DekUnrealGameAudit.Core;
using Microsoft.Win32;

namespace DekUnrealGameAudit.Gui.Views;

public partial class HashHeaderFilesView : UserControl, ISharedFieldView {
    private AppSettings _settings = null!;
    private CancellationTokenSource? _cts;
    /// <summary>Set only when hashing succeeded but writing the result failed - see HashGameFilesView's
    /// copy of this field for why.</summary>
    private HeaderManifest? _pendingManifest;
    private string? _lastWrittenPath;

    /// <summary>True while a Run is in progress - see HashGameFilesView.IsBusy for why MainWindow uses this.</summary>
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    /// <summary>Raised whenever the UE4SS folder changes here - also shown (and editable) on Quick Actions. See
    /// HashGameFilesView's copy of this event for why views need to stay in sync rather than each keeping an
    /// independent copy.</summary>
    public event EventHandler<SharedFieldChangedEventArgs>? SharedFieldChanged;

    /// <summary>See HashGameFilesView's copy of these events for why this is an explicit action.</summary>
    public event EventHandler<string>? UseAsOldRequested;
    public event EventHandler<string>? UseAsNewRequested;

    private void SetBusy(bool busy) {
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public HashHeaderFilesView() {
        InitializeComponent();
        Ue4ssFolderBox.TextChanged += (_, _) => {
            SharedFieldChanged?.Invoke(this, new(SharedField.HeaderDumpFolder, Ue4ssFolderBox.Text.Trim()));
            Ue4ssFolderBox.SetFieldValid(SharedFieldValidation.IsExistingFolderOrBlank(Ue4ssFolderBox.Text));
        };
    }

    public void Initialize(AppSettings settings) {
        _settings = settings;
        ApplyFromSettings();
    }

    public void ApplyFromSettings() {
        Ue4ssFolderBox.Text = _settings.HeaderDumpFolder ?? "";
        OutputPathBox.Text = _settings.HeaderOutputPath ?? "";
    }

    public void CaptureToSettings() {
        _settings.HeaderDumpFolder = Ue4ssFolderBox.Text.Trim();
        _settings.HeaderOutputPath = OutputPathBox.Text.Trim();
    }

    /// <summary>Applies a shared field pushed from another view (see ISharedFieldView) without touching this
    /// view's own distinct Output file field.</summary>
    public void SetSharedField(string fieldName, string value) {
        if (fieldName == SharedField.HeaderDumpFolder)
            Ue4ssFolderBox.Text = value;
    }

    private void BrowseUe4ssFolder_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFolderDialog { Title = "Select UE4SS's own output folder (next to UE4SS.dll)" };
        if (dialog.ShowDialog() == true)
            Ue4ssFolderBox.Text = dialog.FolderName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e) {
        var dialog = new SaveFileDialog { Title = "Save header manifest as", Filter = "JSON files (*.json)|*.json", FileName = "header-manifest.json" };
        if (dialog.ShowDialog() == true)
            OutputPathBox.Text = dialog.FileName;
    }

    private async void Run_Click(object sender, RoutedEventArgs e) {
        var ue4ssFolder = Ue4ssFolderBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(ue4ssFolder)) {
            const string message = "UE4SS folder is required.";
            AppendLog(message);
            ProgressText.Text = message;
            return;
        }

        // See HashGameFilesView.Run_Click for why only an explicit output path is claimed.
        if (OperationUiHelpers.TryClaimOutput(OutputPathBox.Text.Trim(), AppendLog) is not { } outputClaim)
            return;

        SetBusy(true);
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        RetrySaveButton.Visibility = Visibility.Collapsed;
        UseAsPanel.Visibility = Visibility.Collapsed;
        FieldsGrid.IsEnabled = false;
        LogBox.Clear();
        ProgressText.Text = OperationStatusText.For(OperationStatus.Running);
        ProgressBar.Value = 0;
        // Parsing only reports two coarse steps (CXX done, UHT done), not a smooth per-file percentage, so
        // an indeterminate (animated, no fixed value) bar is a more honest "working..." indicator than one
        // trying to map that onto a 0-100% fill.
        ProgressBar.IsIndeterminate = true;
        _pendingManifest = null;
        _lastWrittenPath = null;
        _cts = new CancellationTokenSource();

        CaptureToSettings();
        _settings.Save();

        var stopwatch = Stopwatch.StartNew();
        IProgress<string> logProgress = new Progress<string>(AppendLog);

        // Computing and saving are deliberately separate try/catch blocks - see HashGameFilesView.Run_Click.
        HeaderManifest? manifest = null;
        try {
            var token = _cts.Token;
            manifest = await Task.Run(() => HeaderHasher.Hash(
                Path.GetFullPath(ue4ssFolder),
                onLog: line => logProgress.Report(line),
                cancellationToken: token), token);
            AppendLog($"CXX types: {manifest.CxxTypes.Count}   CXX enums: {manifest.CxxEnums.Count}   UHT types: {manifest.UhtTypes.Count}");
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

    private async Task TrySaveManifest(HeaderManifest manifest, string outPathInput, Stopwatch stopwatch) {
        ProgressText.Text = OperationStatusText.For(OperationStatus.Saving);
        try {
            var finalOutPath = await Task.Run(() => {
                var path = !string.IsNullOrWhiteSpace(outPathInput)
                    ? outPathInput
                    : AutoFileNaming.AllocateUniquePath(HeaderHasher.BuildAutoFileName);
                JsonUtil.WriteFile(path, manifest);
                return path;
            });

            ProgressBar.Maximum = 1;
            ProgressBar.Value = 1;
            ProgressText.Text = $"{OperationStatusText.For(OperationStatus.Completed)} in {OperationUiHelpers.FormatElapsed(stopwatch.Elapsed)}";
            AppendLog($"Wrote header manifest to {Path.GetFullPath(finalOutPath)} in {OperationUiHelpers.FormatElapsed(stopwatch.Elapsed)}");
            _pendingManifest = null;
            RetrySaveButton.Visibility = Visibility.Collapsed;
            _lastWrittenPath = Path.GetFullPath(finalOutPath);
            UseAsPanel.Visibility = Visibility.Visible;
        } catch (Exception ex) {
            AppendLog($"Error saving manifest: {ex.Message}");
            ProgressText.Text = OperationStatusText.For(OperationStatus.FailedToSave);
            _pendingManifest = manifest;
            RetrySaveButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>See HashGameFilesView's copy of this handler pair for why this is explicit.</summary>
    private void UseAsOld_Click(object sender, RoutedEventArgs e) {
        if (_lastWrittenPath != null)
            UseAsOldRequested?.Invoke(this, _lastWrittenPath);
    }

    private void UseAsNew_Click(object sender, RoutedEventArgs e) {
        if (_lastWrittenPath != null)
            UseAsNewRequested?.Invoke(this, _lastWrittenPath);
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
