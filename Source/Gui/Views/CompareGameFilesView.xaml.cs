using System.IO;
using System.Windows;
using System.Windows.Controls;
using DekUnrealGameAudit.Core;
using Microsoft.Win32;

namespace DekUnrealGameAudit.Gui.Views;

public partial class CompareGameFilesView : UserControl {
    private AppSettings _settings = null!;
    private CancellationTokenSource? _cts;
    private GameCompareResult? _lastResult;
    private string _lastOldManifestPath = "";
    private string _lastNewManifestPath = "";
    private string _lastOutputPath = "";
    private CompareResultsPanel _results = null!;

    /// <summary>Raised with the written comparison.json's path after a successful run, so MainWindow can
    /// auto-fill Compare Mod Files' "Diff file" field with it - the natural next step after comparing two
    /// game manifests.</summary>
    public event EventHandler<string>? CompareProduced;

    /// <summary>True while a Run is in progress - see HashGameFilesView.IsBusy for why MainWindow uses this.</summary>
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    private void SetBusy(bool busy) {
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public CompareGameFilesView() {
        InitializeComponent();
        // A result stays exportable only until its own inputs change - editing either manifest path (by
        // hand, browsing, or a profile switch calling ApplyFromSettings) after a run means Export Markdown
        // would otherwise keep describing files that no longer match what's shown on screen.
        OldManifestBox.TextChanged += (_, _) => InvalidateResultIfAny();
        NewManifestBox.TextChanged += (_, _) => InvalidateResultIfAny();
        _results = new CompareResultsPanel(ResultsListView, ResultsSearchBox, ResultsEmptyHint,
            "Run a comparison to see added/removed/changed assets here, with counts above and a full list below.");
    }

    private static List<CompareResultRow> BuildResultRows(GameCompareResult result) {
        var rows = new List<CompareResultRow>();
        foreach (var u in result.Uncertain)
            rows.Add(new CompareResultRow("Uncertain", u.Path, u.Reason));
        foreach (var path in result.Added)
            rows.Add(new CompareResultRow("Added", path, ""));
        foreach (var path in result.Removed)
            rows.Add(new CompareResultRow("Removed", path, ""));
        foreach (var c in result.Changed)
            rows.Add(new CompareResultRow("Changed", c.Path, $"{ShortHash(c.OldHash)} -> {ShortHash(c.NewHash)}"));
        return rows;
    }

    private static string ShortHash(string hash) => hash.Length > 8 ? hash[..8] : hash;

    private void InvalidateResultIfAny() {
        if (_lastResult == null)
            return;
        _lastResult = null;
        ExportMarkdownButton.IsEnabled = false;
        AppendLog("Inputs changed since the last run - Export Markdown disabled until you run again.");
    }

    public void Initialize(AppSettings settings) {
        _settings = settings;
        ApplyFromSettings();
    }

    public void ApplyFromSettings() {
        OldManifestBox.Text = _settings.OldManifestPath ?? "";
        NewManifestBox.Text = _settings.NewManifestPath ?? "";
        OutputPathBox.Text = _settings.GameCompareOutputPath ?? "";
    }

    public void CaptureToSettings() {
        _settings.OldManifestPath = OldManifestBox.Text.Trim();
        _settings.NewManifestPath = NewManifestBox.Text.Trim();
        _settings.GameCompareOutputPath = OutputPathBox.Text.Trim();
    }

    /// <summary>Fills the Old/New manifest field from Hash Game Files' explicit "Use as Old"/"Use as New"
    /// action - see HashGameFilesView.UseAsOldRequested for why this is a click, not an automatic handoff.
    /// Skipped while busy, same as CompareModFilesView.SetGameComparePath.</summary>
    public void SetOldManifestPath(string path) {
        if (IsBusy) {
            AppendLog($"A new old-manifest candidate is ready ({path}) but wasn't filled in here - a run is in progress.");
            return;
        }
        OldManifestBox.Text = path;
        _settings.OldManifestPath = path;
    }

    public void SetNewManifestPath(string path) {
        if (IsBusy) {
            AppendLog($"A new new-manifest candidate is ready ({path}) but wasn't filled in here - a run is in progress.");
            return;
        }
        NewManifestBox.Text = path;
        _settings.NewManifestPath = path;
    }

    private void BrowseOld_Click(object sender, RoutedEventArgs e) => BrowseManifest(OldManifestBox);
    private void BrowseNew_Click(object sender, RoutedEventArgs e) => BrowseManifest(NewManifestBox);

    private static void BrowseManifest(TextBox target) {
        var dialog = new OpenFileDialog { Title = "Select a game manifest", Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
            target.Text = dialog.FileName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e) {
        var dialog = new SaveFileDialog { Title = "Save comparison as", Filter = "JSON files (*.json)|*.json", FileName = "comparison.json" };
        if (dialog.ShowDialog() == true)
            OutputPathBox.Text = dialog.FileName;
    }

    private async void Run_Click(object sender, RoutedEventArgs e) {
        var oldPath = OldManifestBox.Text.Trim();
        var newPath = NewManifestBox.Text.Trim();
        var outPath = OutputPathBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath) || string.IsNullOrWhiteSpace(outPath)) {
            const string message = "Old manifest, new manifest and output file are all required.";
            AppendLog(message);
            StatusText.Text = message;
            return;
        }

        try {
            PathCollisionGuard.CheckNoCollisions(
                inputs: [("old manifest", oldPath), ("new manifest", newPath)],
                outputs: [("output file", outPath)]);
        } catch (Exception ex) {
            AppendLog(ex.Message);
            return;
        }

        // Claims outPath across all six tabs - see HashGameFilesView.Run_Click.
        IDisposable outputClaim;
        try {
            outputClaim = OperationCoordinator.Claim(outPath);
        } catch (Exception ex) {
            AppendLog(ex.Message);
            return;
        }

        SetBusy(true);
        RunButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        FieldsGrid.IsEnabled = false;
        ExportMarkdownButton.IsEnabled = false;
        LogBox.Clear();
        SummaryText.Text = "";
        StatusText.Text = OperationStatusText.For(OperationStatus.Running);
        _lastResult = null;
        _results.Clear();
        _cts = new CancellationTokenSource();

        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            AppendLog("WARN: old and new manifest are the same file - this will compare it against itself.");

        CaptureToSettings();
        _settings.Save();

        IProgress<string> logProgress = new Progress<string>(AppendLog);

        // Computing and saving are deliberately separate try/catch blocks - see HashGameFilesView.Run_Click.
        // No Retry Save button here (unlike the hash views) - re-running this Compare is cheap, so clicking
        // Run again after fixing whatever broke the save is a perfectly good retry path.
        GameCompareResult? result = null;
        try {
            var token = _cts.Token;
            result = await Task.Run(() => {
                var oldManifest = JsonUtil.ReadFile<GameManifest>(oldPath);
                var newManifest = JsonUtil.ReadFile<GameManifest>(newPath);
                var r = GameCompareEngine.Compare(oldManifest, newManifest,
                    onLog: line => logProgress.Report(line), cancellationToken: token);
                r.OldManifestPath = Path.GetFullPath(oldPath);
                r.NewManifestPath = Path.GetFullPath(newPath);
                r.OldUeVersion = oldManifest.UeVersion;
                r.NewUeVersion = newManifest.UeVersion;
                return r;
            }, token);

            if (result.Added.Count == 0 && result.Removed.Count == 0 && result.Changed.Count == 0 && result.Uncertain.Count == 0) {
                AppendLog("No differences - the two manifests match.");
            } else {
                if (result.Uncertain.Count > 0) {
                    AppendLog($"⚠ Uncertain ({result.Uncertain.Count}) - review before trusting this comparison:");
                    foreach (var line in LogCapping.Cap(result.Uncertain, u => $"  ? {u.Path} - {u.Reason}"))
                        AppendLog(line);
                }
                if (result.Added.Count > 0) {
                    AppendLog($"Added ({result.Added.Count}):");
                    foreach (var line in LogCapping.Cap(result.Added, path => $"  + {path}"))
                        AppendLog(line);
                }
                if (result.Removed.Count > 0) {
                    AppendLog($"Removed ({result.Removed.Count}):");
                    foreach (var line in LogCapping.Cap(result.Removed, path => $"  - {path}"))
                        AppendLog(line);
                }
                if (result.Changed.Count > 0) {
                    AppendLog($"Changed ({result.Changed.Count}):");
                    foreach (var line in LogCapping.Cap(result.Changed, changed => $"  ~ {changed.Path}"))
                        AppendLog(line);
                }
            }
        } catch (OperationCanceledException) {
            AppendLog("Cancelled - no comparison was written.");
            StatusText.Text = OperationStatusText.For(OperationStatus.Cancelled);
            result = null;
        } catch (Exception ex) {
            AppendLog($"Error: {ex.Message}");
            StatusText.Text = OperationStatusText.For(OperationStatus.Failed);
            result = null;
        }

        if (result != null && !_cts.IsCancellationRequested) {
            StatusText.Text = OperationStatusText.For(OperationStatus.Saving);
            try {
                var token = _cts.Token;
                await Task.Run(() => {
                    token.ThrowIfCancellationRequested();
                    JsonUtil.WriteFile(outPath, result);
                }, token);

                SummaryText.Text = $"Added: {result.Added.Count}   Removed: {result.Removed.Count}   " +
                    $"Changed: {result.Changed.Count}   Unchanged: {result.UnchangedCount}" +
                    (result.Uncertain.Count > 0 ? $"   ⚠ Uncertain: {result.Uncertain.Count}" : "");
                _results.SetRows(BuildResultRows(result));
                AppendLog($"Wrote comparison to {Path.GetFullPath(outPath)}");
                StatusText.Text = result.Uncertain.Count > 0
                    ? OperationStatusText.For(OperationStatus.Partial, $"{result.Uncertain.Count} uncertain")
                    : OperationStatusText.For(OperationStatus.Completed);
                CompareProduced?.Invoke(this, outPath);
                _lastResult = result;
                _lastOldManifestPath = oldPath;
                _lastNewManifestPath = newPath;
                _lastOutputPath = outPath;
                ExportMarkdownButton.IsEnabled = true;
            } catch (OperationCanceledException) {
                AppendLog("Cancelled before saving - no comparison was written.");
                StatusText.Text = OperationStatusText.For(OperationStatus.Cancelled);
            } catch (Exception ex) {
                AppendLog($"Error saving comparison: {ex.Message}");
                StatusText.Text = OperationStatusText.For(OperationStatus.FailedToSave);
            }
        }

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

    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e) {
        if (_lastResult == null)
            return;

        var dialog = new SaveFileDialog { Title = "Save Markdown report as", Filter = "Markdown files (*.md)|*.md", FileName = "comparison-report.md" };
        if (dialog.ShowDialog() != true)
            return;

        try {
            PathCollisionGuard.CheckNoCollisions(
                inputs: [("old manifest", _lastOldManifestPath), ("new manifest", _lastNewManifestPath)],
                outputs: [("JSON output", _lastOutputPath), ("Markdown report", dialog.FileName)]);

            // Rendering and writing a huge result synchronously here would freeze the UI - this handler
            // isn't behind the busy-lock the Run button uses, so re-disable/re-enable around the await.
            ExportMarkdownButton.IsEnabled = false;
            var result = _lastResult;
            await Task.Run(() => AtomicFile.WriteAllText(dialog.FileName, GameCompareEngine.RenderMarkdown(result)));
            _settings.GameCompareMdOutputPath = dialog.FileName;
            AppendLog($"Wrote Markdown report to {dialog.FileName}");
        } catch (Exception ex) {
            AppendLog($"Error writing Markdown report: {ex.Message}");
        } finally {
            ExportMarkdownButton.IsEnabled = true;
        }
    }

    /// <summary>See HashGameFilesView.CopyLog_Click for why this exists alongside normal text selection.</summary>
    private void CopyLog_Click(object sender, RoutedEventArgs e) => LogViewHelper.CopyAll(LogBox);

    private void AppendLog(string line) => LogViewHelper.Append(LogBox, LogScrollViewer, line);
}
