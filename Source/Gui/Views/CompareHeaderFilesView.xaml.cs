using System.IO;
using System.Windows;
using System.Windows.Controls;
using DekUnrealGameAudit.Core;
using Microsoft.Win32;

namespace DekUnrealGameAudit.Gui.Views;

public partial class CompareHeaderFilesView : UserControl {
    private AppSettings _settings = null!;
    private CancellationTokenSource? _cts;
    private HeaderCompareResult? _lastResult;
    private string _lastOldManifestPath = "";
    private string _lastNewManifestPath = "";
    private string _lastOutputPath = "";
    private CompareResultsPanel _results = null!;

    /// <summary>True while a Run is in progress - see HashGameFilesView.IsBusy for why MainWindow uses this.</summary>
    public bool IsBusy { get; private set; }
    public event EventHandler? BusyChanged;

    private void SetBusy(bool busy) {
        IsBusy = busy;
        BusyChanged?.Invoke(this, EventArgs.Empty);
    }

    public CompareHeaderFilesView() {
        InitializeComponent();
        // See CompareGameFilesView's copy of this wiring for why.
        OldManifestBox.TextChanged += (_, _) => InvalidateResultIfAny();
        NewManifestBox.TextChanged += (_, _) => InvalidateResultIfAny();
        _results = new CompareResultsPanel(ResultsListView, ResultsSearchBox, ResultsEmptyHint,
            "Run a comparison to see CXX memory-layout and UHT reflection changes between two header dumps.");
    }

    private static List<CompareResultRow> BuildResultRows(HeaderCompareResult result) {
        var rows = new List<CompareResultRow>();
        foreach (var name in result.CxxAddedTypes) rows.Add(new CompareResultRow("CXX type added", name, ""));
        foreach (var name in result.CxxRemovedTypes) rows.Add(new CompareResultRow("CXX type removed", name, ""));
        foreach (var c in result.CxxChangedTypes)
            rows.Add(new CompareResultRow("CXX type changed", c.TypeName, $"{c.FieldChanges.Count} field change(s)"));
        foreach (var name in result.CxxAddedEnums) rows.Add(new CompareResultRow("CXX enum added", name, ""));
        foreach (var name in result.CxxRemovedEnums) rows.Add(new CompareResultRow("CXX enum removed", name, ""));
        foreach (var c in result.CxxChangedEnums)
            rows.Add(new CompareResultRow("CXX enum changed", c.EnumName, $"{c.ValueChanges.Count} value change(s)"));
        foreach (var name in result.UhtAddedTypes) rows.Add(new CompareResultRow("UHT type added", name, ""));
        foreach (var name in result.UhtRemovedTypes) rows.Add(new CompareResultRow("UHT type removed", name, ""));
        foreach (var c in result.UhtChangedTypes)
            rows.Add(new CompareResultRow("UHT type changed", c.TypeName, $"{c.MemberChanges.Count} member change(s)"));
        return rows;
    }

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
        OldManifestBox.Text = _settings.OldHeaderManifestPath ?? "";
        NewManifestBox.Text = _settings.NewHeaderManifestPath ?? "";
        OutputPathBox.Text = _settings.HeaderCompareOutputPath ?? "";
    }

    public void CaptureToSettings() {
        _settings.OldHeaderManifestPath = OldManifestBox.Text.Trim();
        _settings.NewHeaderManifestPath = NewManifestBox.Text.Trim();
        _settings.HeaderCompareOutputPath = OutputPathBox.Text.Trim();
    }

    /// <summary>See CompareGameFilesView's copy of these methods for why this is an explicit-action target,
    /// skipped while busy.</summary>
    public void SetOldManifestPath(string path) {
        if (IsBusy) {
            AppendLog($"A new old-manifest candidate is ready ({path}) but wasn't filled in here - a run is in progress.");
            return;
        }
        OldManifestBox.Text = path;
        _settings.OldHeaderManifestPath = path;
    }

    public void SetNewManifestPath(string path) {
        if (IsBusy) {
            AppendLog($"A new new-manifest candidate is ready ({path}) but wasn't filled in here - a run is in progress.");
            return;
        }
        NewManifestBox.Text = path;
        _settings.NewHeaderManifestPath = path;
    }

    private void BrowseOld_Click(object sender, RoutedEventArgs e) => BrowseManifest(OldManifestBox);
    private void BrowseNew_Click(object sender, RoutedEventArgs e) => BrowseManifest(NewManifestBox);

    private static void BrowseManifest(TextBox target) {
        var dialog = new OpenFileDialog { Title = "Select a header manifest", Filter = "JSON files (*.json)|*.json" };
        if (dialog.ShowDialog() == true)
            target.Text = dialog.FileName;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e) {
        var dialog = new SaveFileDialog { Title = "Save header comparison as", Filter = "JSON files (*.json)|*.json", FileName = "header-comparison.json" };
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
        CxxSummaryText.Text = "";
        UhtSummaryText.Text = "";
        StatusText.Text = OperationStatusText.For(OperationStatus.Running);
        _lastResult = null;
        _results.Clear();
        _cts = new CancellationTokenSource();

        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            AppendLog("WARN: old and new manifest are the same file - this will compare it against itself.");

        CaptureToSettings();
        _settings.Save();

        // Computing and saving are deliberately separate try/catch blocks - see HashGameFilesView.Run_Click.
        // No Retry Save button here - re-running this Compare is cheap.
        HeaderCompareResult? result = null;
        try {
            var token = _cts.Token;
            List<string> coverageWarnings;
            // Compare is fast/synchronous (no I/O), so its onLog is captured into a plain list inside Task.Run
            // rather than Dispatcher.Invoke-ing AppendLog live - no other call in this view's background work
            // touches the UI mid-run either; warnings are appended once, back on the UI thread, right below.
            (result, coverageWarnings) = await Task.Run(() => {
                var oldManifest = JsonUtil.ReadFile<HeaderManifest>(oldPath);
                var newManifest = JsonUtil.ReadFile<HeaderManifest>(newPath);
                var warnings = new List<string>();
                var r = HeaderCompareEngine.Compare(oldManifest, newManifest,
                    onLog: warnings.Add, cancellationToken: token);
                r.OldManifestPath = Path.GetFullPath(oldPath);
                r.NewManifestPath = Path.GetFullPath(newPath);
                return (r, warnings);
            }, token);

            foreach (var warning in coverageWarnings)
                AppendLog(warning);

            CxxSummaryText.Text = (result.CxxCoverageKnown ? "" : "⚠ Coverage unknown - ") +
                $"Memory layout - Added: {result.CxxAddedTypes.Count}   Removed: {result.CxxRemovedTypes.Count}   " +
                $"Changed: {result.CxxChangedTypes.Count}   Unchanged: {result.CxxUnchangedTypeCount}   " +
                $"(enums - Added: {result.CxxAddedEnums.Count}   Removed: {result.CxxRemovedEnums.Count}   Changed: {result.CxxChangedEnums.Count})";
            UhtSummaryText.Text = (result.UhtCoverageKnown ? "" : "⚠ Coverage unknown - ") +
                $"Source/reflection - Added: {result.UhtAddedTypes.Count}   Removed: {result.UhtRemovedTypes.Count}   " +
                $"Changed: {result.UhtChangedTypes.Count}   Unchanged: {result.UhtUnchangedTypeCount}";
            _results.SetRows(BuildResultRows(result));

            LogChanges(result);
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
                AppendLog($"Wrote header comparison to {Path.GetFullPath(outPath)}");
                StatusText.Text = (!result.CxxCoverageKnown || !result.UhtCoverageKnown)
                    ? OperationStatusText.For(OperationStatus.Partial, "coverage unknown - see log")
                    : OperationStatusText.For(OperationStatus.Completed);
                _lastResult = result;
                _lastOldManifestPath = oldPath;
                _lastNewManifestPath = newPath;
                _lastOutputPath = outPath;
                ExportMarkdownButton.IsEnabled = true;
            } catch (OperationCanceledException) {
                AppendLog("Cancelled before saving - no header comparison was written.");
                StatusText.Text = OperationStatusText.For(OperationStatus.Cancelled);
            } catch (Exception ex) {
                AppendLog($"Error saving header comparison: {ex.Message}");
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

    private void LogChanges(HeaderCompareResult result) {
        if (result.CxxAddedTypes.Count > 0) {
            AppendLog($"CXX types added ({result.CxxAddedTypes.Count}):");
            foreach (var line in LogCapping.Cap(result.CxxAddedTypes, name => $"  + {name}")) AppendLog(line);
        }
        if (result.CxxRemovedTypes.Count > 0) {
            AppendLog($"CXX types removed ({result.CxxRemovedTypes.Count}):");
            foreach (var line in LogCapping.Cap(result.CxxRemovedTypes, name => $"  - {name}")) AppendLog(line);
        }
        foreach (var typeChange in TakeCapped(result.CxxChangedTypes, "CXX type")) {
            AppendLog($"CXX type changed: {typeChange.TypeName}");
            if (typeChange.OldTotalSize != null)
                AppendLog($"  size {typeChange.OldTotalSize} -> {typeChange.NewTotalSize}");
            if (typeChange.OldParent != null || typeChange.NewParent != null)
                AppendLog($"  parent {typeChange.OldParent ?? "(none)"} -> {typeChange.NewParent ?? "(none)"}");
            foreach (var fc in typeChange.FieldChanges) {
                AppendLog(fc.ChangeType switch {
                    CxxFieldChangeType.Added => $"  + {fc.Field}",
                    CxxFieldChangeType.Removed => $"  - {fc.Field}",
                    CxxFieldChangeType.OffsetShifted => $"  ⚠ {fc.Field} {HeaderCompareEngine.DescribeFieldChange(fc)}",
                    _ => $"  {fc.Field} {HeaderCompareEngine.DescribeFieldChange(fc)}",
                });
            }
            if (typeChange.OldUnsupportedFieldLines.Count > 0 || typeChange.NewUnsupportedFieldLines.Count > 0)
                AppendLog("  ⚠ unsupported field declaration(s) changed (not structurally diffed) - see the exported report");
        }

        if (result.CxxAddedEnums.Count > 0) {
            AppendLog($"CXX enums added ({result.CxxAddedEnums.Count}):");
            foreach (var line in LogCapping.Cap(result.CxxAddedEnums, name => $"  + {name}")) AppendLog(line);
        }
        if (result.CxxRemovedEnums.Count > 0) {
            AppendLog($"CXX enums removed ({result.CxxRemovedEnums.Count}):");
            foreach (var line in LogCapping.Cap(result.CxxRemovedEnums, name => $"  - {name}")) AppendLog(line);
        }
        foreach (var enumChange in TakeCapped(result.CxxChangedEnums, "CXX enum")) {
            AppendLog($"CXX enum changed: {enumChange.EnumName}");
            foreach (var vc in enumChange.ValueChanges) {
                AppendLog(vc.ChangeType switch {
                    CxxEnumValueChangeType.ValueChanged => $"  ⚠ {vc.Name} {vc.OldValue} -> {vc.NewValue}",
                    CxxEnumValueChangeType.Added => $"  + {vc.Name} = {vc.NewValue}",
                    CxxEnumValueChangeType.Removed => $"  - {vc.Name} (was {vc.OldValue})",
                    _ => $"  {vc.Name}",
                });
            }
        }

        if (result.UhtAddedTypes.Count > 0) {
            AppendLog($"UHT types added ({result.UhtAddedTypes.Count}):");
            foreach (var line in LogCapping.Cap(result.UhtAddedTypes, name => $"  + {name}")) AppendLog(line);
        }
        if (result.UhtRemovedTypes.Count > 0) {
            AppendLog($"UHT types removed ({result.UhtRemovedTypes.Count}):");
            foreach (var line in LogCapping.Cap(result.UhtRemovedTypes, name => $"  - {name}")) AppendLog(line);
        }
        foreach (var typeChange in TakeCapped(result.UhtChangedTypes, "UHT type")) {
            AppendLog($"UHT type changed: {typeChange.TypeName}");
            if (typeChange.OldParent != null || typeChange.NewParent != null)
                AppendLog($"  parent {typeChange.OldParent ?? "(none)"} -> {typeChange.NewParent ?? "(none)"}");
            if (typeChange.OldKind != null || typeChange.NewKind != null)
                AppendLog($"  ⚠ kind {typeChange.OldKind} -> {typeChange.NewKind}");
            if (typeChange.OldSpecifiers != null || typeChange.NewSpecifiers != null)
                AppendLog($"  specifiers '{typeChange.OldSpecifiers}' -> '{typeChange.NewSpecifiers}'");
            foreach (var mc in typeChange.MemberChanges) {
                AppendLog(mc.ChangeType switch {
                    UhtMemberChangeType.Added => $"  + {mc.Kind} {mc.Member} = {mc.NewSignature}",
                    UhtMemberChangeType.Removed => $"  - {mc.Kind} {mc.Member} (was {mc.OldSignature})",
                    _ => $"  ~ {mc.Kind} {mc.Member}: {mc.OldSignature} -> {mc.NewSignature}",
                });
            }
        }
    }

    private async void ExportMarkdown_Click(object sender, RoutedEventArgs e) {
        if (_lastResult == null)
            return;

        var dialog = new SaveFileDialog { Title = "Save Markdown report as", Filter = "Markdown files (*.md)|*.md", FileName = "header-comparison-report.md" };
        if (dialog.ShowDialog() != true)
            return;

        try {
            PathCollisionGuard.CheckNoCollisions(
                inputs: [("old manifest", _lastOldManifestPath), ("new manifest", _lastNewManifestPath)],
                outputs: [("JSON output", _lastOutputPath), ("Markdown report", dialog.FileName)]);

            ExportMarkdownButton.IsEnabled = false;
            var result = _lastResult;
            await Task.Run(() => AtomicFile.WriteAllText(dialog.FileName, HeaderCompareEngine.RenderMarkdown(result)));
            _settings.HeaderCompareMdOutputPath = dialog.FileName;
            AppendLog($"Wrote Markdown report to {dialog.FileName}");
        } catch (Exception ex) {
            AppendLog($"Error writing Markdown report: {ex.Message}");
        } finally {
            ExportMarkdownButton.IsEnabled = true;
        }
    }

    /// <summary>Caps how many of a "changed types/enums" collection get their full detail logged - each
    /// item here can itself log several lines (size/parent/per-field changes), so this bounds the total
    /// rather than <see cref="LogCapping.Cap{T}"/>'s one-line-per-item case.</summary>
    private IEnumerable<T> TakeCapped<T>(IReadOnlyCollection<T> items, string label) {
        var shown = 0;
        foreach (var item in items) {
            if (shown >= LogCapping.DefaultMaxLines) {
                AppendLog($"  ... and {items.Count - shown} more changed {label}(s) - see the full exported report.");
                yield break;
            }
            yield return item;
            shown++;
        }
    }

    /// <summary>See HashGameFilesView.CopyLog_Click for why this exists alongside normal text selection.</summary>
    private void CopyLog_Click(object sender, RoutedEventArgs e) => LogViewHelper.CopyAll(LogBox);

    private void AppendLog(string line) => LogViewHelper.Append(LogBox, LogScrollViewer, line);
}
