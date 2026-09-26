using System.IO;
using System.Windows;
using System.Windows.Controls;
using DekUnrealGameAudit.Core;
using Microsoft.Win32;

namespace DekUnrealGameAudit.Gui.Views;

public partial class CompareModFilesView : UserControl {
    private AppSettings _settings = null!;
    private CancellationTokenSource? _cts;
    private ModsCompareReport? _lastReport;
    private string _lastGameComparePath = "";
    private string _lastModsManifestPath = "";
    private string _lastJsonOutputPath = "";
    private CompareResultsPanel _results = null!;

    /// <summary>True while a Run is in progress - see HashGameFilesView.IsBusy for why MainWindow uses this.</summary>
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    private void SetBusy(bool busy) {
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public CompareModFilesView() {
        InitializeComponent();
        // See CompareGameFilesView's copy of this wiring for why.
        GameComparePathBox.TextChanged += (_, _) => InvalidateResultIfAny();
        ModsManifestBox.TextChanged += (_, _) => InvalidateResultIfAny();
        _results = new CompareResultsPanel(ResultsListView, ResultsSearchBox, ResultsEmptyHint,
            "Run a comparison to see which mods are affected by the game update, and which need a closer look.");
    }

    private static List<CompareResultRow> BuildResultRows(ModsCompareReport report) {
        var rows = new List<CompareResultRow>();
        foreach (var (modName, entry) in report.Mods.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase)) {
            var category = entry.NeedsUpdate ? "Needs update" : entry.NeedsReview ? "Needs review" : "Up to date";
            var detail = entry.NeedsUpdate
                ? $"{entry.AffectedAssets.Count} affected asset(s)"
                : entry.NeedsReview
                    ? $"{entry.UncertainAssets.Count} uncertain, {entry.CollidingAssets.Count} new collision(s)"
                    : "";
            rows.Add(new CompareResultRow(category, modName, detail));
        }
        return rows;
    }

    private void InvalidateResultIfAny() {
        if (_lastReport == null)
            return;
        _lastReport = null;
        ExportMarkdownButton.IsEnabled = false;
        AppendLog("Inputs changed since the last run - Export Markdown disabled until you run again.");
    }

    public void Initialize(AppSettings settings) {
        _settings = settings;
        ApplyFromSettings();
    }

    public void ApplyFromSettings() {
        GameComparePathBox.Text = _settings.GameComparePath ?? "";
        ModsManifestBox.Text = _settings.ModsManifestPath ?? "";
        JsonOutputBox.Text = _settings.ModsCompareJsonOutputPath ?? "";
    }

    public void CaptureToSettings() {
        _settings.GameComparePath = GameComparePathBox.Text.Trim();
        _settings.ModsManifestPath = ModsManifestBox.Text.Trim();
        _settings.ModsCompareJsonOutputPath = JsonOutputBox.Text.Trim();
    }

    /// <summary>Auto-fills the comparison file field with a just-produced comparison.json from the Compare
    /// Game Files tab, without touching this view's other fields (a full ApplyFromSettings() here would
    /// clobber e.g. an unsaved, not-yet-captured edit to the JSON/Markdown report paths). Skipped while this
    /// tab is itself busy - overwriting a field that's disabled for the duration of a run this tab already
    /// started would be confusing, and the run itself is unaffected either way since Run_Click already
    /// snapshotted its own inputs before this could fire.</summary>
    public void SetGameComparePath(string path) {
        if (IsBusy) {
            AppendLog($"A new comparison file is ready ({path}) but wasn't auto-filled here - a run is in progress.");
            return;
        }
        GameComparePathBox.Text = path;
        _settings.GameComparePath = path;
    }

    /// <summary>Auto-fills the mods manifest field with a just-produced mods-manifest.json from the Hash Mod
    /// Files tab - see <see cref="SetGameComparePath"/> for why this only touches the one field and is
    /// skipped while busy.</summary>
    public void SetModsManifestPath(string path) {
        if (IsBusy) {
            AppendLog($"A new mods manifest is ready ({path}) but wasn't auto-filled here - a run is in progress.");
            return;
        }
        ModsManifestBox.Text = path;
        _settings.ModsManifestPath = path;
    }

    private void BrowseGameCompare_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog { Title = "Select a game comparison file", Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
            GameComparePathBox.Text = dialog.FileName;
    }

    private void BrowseModsManifest_Click(object sender, RoutedEventArgs e) {
        var dialog = new OpenFileDialog { Title = "Select a mods manifest", Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
            ModsManifestBox.Text = dialog.FileName;
    }

    private void BrowseJsonOutput_Click(object sender, RoutedEventArgs e) {
        var dialog = new SaveFileDialog { Title = "Save JSON report as", Filter = "JSON files (*.json)|*.json", FileName = "mod-compare.json" };
        if (dialog.ShowDialog() == true)
            JsonOutputBox.Text = dialog.FileName;
    }

    private async void Run_Click(object sender, RoutedEventArgs e) {
        var gameComparePath = GameComparePathBox.Text.Trim();
        var modsPath = ModsManifestBox.Text.Trim();
        var jsonOutPath = JsonOutputBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(gameComparePath) || string.IsNullOrWhiteSpace(modsPath)) {
            const string message = "Comparison file and mods manifest are both required.";
            AppendLog(message);
            StatusText.Text = message;
            return;
        }

        try {
            PathCollisionGuard.CheckNoCollisions(
                inputs: [("comparison file", gameComparePath), ("mods manifest", modsPath)],
                outputs: [("JSON report", jsonOutPath)]);
        } catch (Exception ex) {
            AppendLog(ex.Message);
            return;
        }

        // Claims jsonOutPath (if any) across all six tabs - see HashGameFilesView.Run_Click.
        IDisposable outputClaim;
        try {
            outputClaim = OperationCoordinator.Claim(jsonOutPath);
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
        _lastReport = null;
        _results.Clear();
        _cts = new CancellationTokenSource();

        CaptureToSettings();
        _settings.Save();

        // Computing and saving are deliberately separate try/catch blocks - see HashGameFilesView.Run_Click.
        // No Retry Save button here - re-running this Compare is cheap.
        ModsCompareReport? report = null;
        var needsReviewCount = 0;
        try {
            var token = _cts.Token;
            List<KeyValuePair<string, ModsCompareEntry>> needsUpdate, needsReview;
            int upToDate, uncertainCount;
            (report, needsUpdate, needsReview, upToDate, uncertainCount) = await Task.Run(() => {
                var diff = JsonUtil.ReadFile<GameCompareResult>(gameComparePath);
                var modManifest = JsonUtil.ReadFile<ModManifest>(modsPath);
                var r = ModsCompareEngine.Compare(diff, modManifest, token);
                r.GameComparePath = Path.GetFullPath(gameComparePath);
                r.ModsManifestPath = Path.GetFullPath(modsPath);
                var (nu, nr, ut) = ModsCompareEngine.Summarize(r);
                return (r, nu, nr, ut, diff.Uncertain.Count);
            }, token);
            needsReviewCount = needsReview.Count;

            SummaryText.Text = $"{needsUpdate.Count} mod(s) potentially affected (review required), {needsReview.Count} need review, {upToDate} unaffected.";
            _results.SetRows(BuildResultRows(report));

            if (uncertainCount > 0)
                AppendLog($"⚠ The game comparison has {uncertainCount} uncertain asset(s) (scan errors on one or both sides) - mods below are flagged for review, not marked confidently unaffected.");

            if (needsUpdate.Count == 0) {
                AppendLog("None confirmed.");
            } else {
                AppendLog("Potentially affected - review required (overrides a changed/removed asset, not proof it's actually broken):");
                var shown = 0;
                foreach (var (modName, entry) in needsUpdate) {
                    if (shown >= LogCapping.DefaultMaxLines) {
                        AppendLog($"  ... and {needsUpdate.Count - shown} more mod(s) - see the full exported report.");
                        break;
                    }
                    AppendLog($"{modName}  ({entry.AffectedAssets.Count} affected asset(s))");
                    foreach (var line in LogCapping.Cap(entry.AffectedAssets, asset => $"  - {asset}"))
                        AppendLog(line);
                    shown++;
                }
            }

            if (needsReview.Count > 0) {
                AppendLog($"Need review ({needsReview.Count}) - scan couldn't confirm these are unaffected, or a new collision was found:");
                var shown = 0;
                foreach (var (modName, entry) in needsReview) {
                    if (shown >= LogCapping.DefaultMaxLines) {
                        AppendLog($"  ... and {needsReview.Count - shown} more mod(s) - see the full exported report.");
                        break;
                    }
                    AppendLog($"{modName}  ({entry.UncertainAssets.Count} uncertain asset(s), {entry.CollidingAssets.Count} new collision(s))");
                    foreach (var line in LogCapping.Cap(entry.UncertainAssets, asset => $"  - uncertain: {asset}"))
                        AppendLog(line);
                    foreach (var line in LogCapping.Cap(entry.CollidingAssets, asset => $"  - colliding with newly added: {asset}"))
                        AppendLog(line);
                    shown++;
                }
            }
        } catch (OperationCanceledException) {
            AppendLog("Cancelled - no report was written.");
            StatusText.Text = OperationStatusText.For(OperationStatus.Cancelled);
            report = null;
        } catch (Exception ex) {
            AppendLog($"Error: {ex.Message}");
            StatusText.Text = OperationStatusText.For(OperationStatus.Failed);
            report = null;
        }

        if (report != null && !_cts.IsCancellationRequested) {
            if (string.IsNullOrWhiteSpace(jsonOutPath)) {
                StatusText.Text = needsReviewCount > 0
                    ? OperationStatusText.For(OperationStatus.Partial, $"{needsReviewCount} need review")
                    : OperationStatusText.For(OperationStatus.Completed);
                _lastReport = report;
                _lastGameComparePath = gameComparePath;
                _lastModsManifestPath = modsPath;
                _lastJsonOutputPath = jsonOutPath;
                ExportMarkdownButton.IsEnabled = true;
            } else {
                StatusText.Text = OperationStatusText.For(OperationStatus.Saving);
                try {
                    var token = _cts.Token;
                    await Task.Run(() => {
                        token.ThrowIfCancellationRequested();
                        JsonUtil.WriteFile(jsonOutPath, report);
                    }, token);
                    AppendLog($"Wrote JSON report to {Path.GetFullPath(jsonOutPath)}");
                    StatusText.Text = needsReviewCount > 0
                        ? OperationStatusText.For(OperationStatus.Partial, $"{needsReviewCount} need review")
                        : OperationStatusText.For(OperationStatus.Completed);
                    _lastReport = report;
                    _lastGameComparePath = gameComparePath;
                    _lastModsManifestPath = modsPath;
                    _lastJsonOutputPath = jsonOutPath;
                    ExportMarkdownButton.IsEnabled = true;
                } catch (OperationCanceledException) {
                    AppendLog("Cancelled before saving - no JSON report was written.");
                    StatusText.Text = OperationStatusText.For(OperationStatus.Cancelled);
                } catch (Exception ex) {
                    AppendLog($"Error saving JSON report: {ex.Message}");
                    StatusText.Text = OperationStatusText.For(OperationStatus.FailedToSave);
                }
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
        if (_lastReport == null)
            return;

        var dialog = new SaveFileDialog { Title = "Save Markdown report as", Filter = "Markdown files (*.md)|*.md", FileName = "mod-update-report.md" };
        if (dialog.ShowDialog() != true)
            return;

        try {
            PathCollisionGuard.CheckNoCollisions(
                inputs: [("comparison file", _lastGameComparePath), ("mods manifest", _lastModsManifestPath)],
                outputs: [("JSON report", _lastJsonOutputPath), ("Markdown report", dialog.FileName)]);

            ExportMarkdownButton.IsEnabled = false;
            var report = _lastReport;
            await Task.Run(() => AtomicFile.WriteAllText(dialog.FileName, ModsCompareEngine.RenderMarkdown(report)));
            _settings.ModsCompareMdOutputPath = dialog.FileName;
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
