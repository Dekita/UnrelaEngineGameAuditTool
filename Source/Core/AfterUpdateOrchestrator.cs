using System.Text;

namespace DekUnrealGameAudit.Core;

/// <summary>CODE-18's "After update" quick action: loads the profile's active baseline (no manual manifest
/// picking), captures a fresh game/mods/header snapshot into a new run directory, runs every comparison that's
/// actually possible given what succeeded on both sides, and atomically saves a combined Markdown + JSON report
/// plus a full diagnostic log. Never touches the active-baseline pointer - an After run never replaces the
/// Before baseline, per CODE-18's explicit requirement.</summary>
public static class AfterUpdateOrchestrator {
    public static QuickActionReport Run(QuickActionOptions options, Action<string>? onLog = null, CancellationToken cancellationToken = default) {
        var logLines = new List<string>();
        void Log(string line) {
            logLines.Add(line);
            onLog?.Invoke(line);
        }

        var baseline = RunDirectoryStore.TryLoadActiveBaseline(options.OutputRoot)
            ?? throw new InvalidOperationException(
                $"No active baseline found under '{options.OutputRoot}'. Run before-update first to create one.");

        var runDirectory = RunDirectoryStore.AllocateRunDirectory(options.OutputRoot);
        var record = QuickActionCapture.CaptureAll(options, runDirectory, Log, cancellationToken);
        RunDirectoryStore.SaveRunRecord(runDirectory, record);
        cancellationToken.ThrowIfCancellationRequested();

        var report = new QuickActionReport {
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            BeforeRunId = baseline.Record.RunId,
            AfterRunId = record.RunId,
        };
        var warnings = new List<string>();
        var skippedOrFailed = new List<string>();

        // CODE-18/UI-09: comparisons below don't know or care whether scope rules changed between the
        // baseline and this run - a rule change can make content that used to be Game now show as Mod (or
        // vice versa), which would otherwise look exactly like a real game/mod content change. Surfacing this
        // as an explicit warning (rather than the fuller "require a deliberate resolution" interactive gate,
        // which is UI-09's own scope) at least stops a scope-rules edit from silently masquerading as one.
        if (baseline.Record.ScopeFingerprint != null && record.ScopeFingerprint != null &&
            !string.Equals(baseline.Record.ScopeFingerprint, record.ScopeFingerprint, StringComparison.Ordinal)) {
            warnings.Add("The baseline used different scope rules than this run - comparisons below may reflect a filter change, not a real game/mod change.");
        }

        GameCompareResult? gameDiff = null;
        string? gameComparePath = null;
        if (baseline.Record.GameStage == StageStatus.Completed && record.GameStage == StageStatus.Completed) {
            var oldGame = JsonUtil.ReadFile<GameManifest>(baseline.Record.GameManifestPath!);
            var newGame = JsonUtil.ReadFile<GameManifest>(record.GameManifestPath!);
            gameDiff = GameCompareEngine.Compare(oldGame, newGame, Log, cancellationToken);
            gameDiff.OldManifestPath = baseline.Record.GameManifestPath!;
            gameDiff.NewManifestPath = record.GameManifestPath!;
            gameDiff.OldUeVersion = oldGame.UeVersion;
            gameDiff.NewUeVersion = newGame.UeVersion;

            cancellationToken.ThrowIfCancellationRequested();
            gameComparePath = Path.Combine(runDirectory, "game-comparison.json");
            JsonUtil.WriteFile(gameComparePath, gameDiff);
            AtomicFile.WriteAllText(Path.Combine(runDirectory, "game-comparison.md"), GameCompareEngine.RenderMarkdown(gameDiff));

            report.GameComparePath = gameComparePath;
            report.GameAdded = gameDiff.Added.Count;
            report.GameRemoved = gameDiff.Removed.Count;
            report.GameChanged = gameDiff.Changed.Count;
            report.GameUnchanged = gameDiff.UnchangedCount;
            report.GameUncertain = gameDiff.Uncertain.Count;
        } else {
            skippedOrFailed.Add("Game compare skipped - the baseline's or this run's game capture did not complete.");
        }

        if (record.ModsStage == StageStatus.Completed) {
            var newMods = JsonUtil.ReadFile<ModManifest>(record.ModsManifestPath!);

            if (gameDiff != null) {
                var modsReport = ModsCompareEngine.Compare(gameDiff, newMods, cancellationToken);
                modsReport.GameComparePath = gameComparePath!;
                modsReport.ModsManifestPath = record.ModsManifestPath!;
                var (needsUpdate, needsReview, upToDate) = ModsCompareEngine.Summarize(modsReport);

                cancellationToken.ThrowIfCancellationRequested();
                var modsReportPath = Path.Combine(runDirectory, "mods-report.json");
                JsonUtil.WriteFile(modsReportPath, modsReport);
                AtomicFile.WriteAllText(Path.Combine(runDirectory, "mods-report.md"), ModsCompareEngine.RenderMarkdown(modsReport));

                report.ModsComparePath = modsReportPath;
                report.ModsNeedingReview = needsUpdate.Count;
                report.ModsNeedingUncertainReview = needsReview.Count;
                report.ModsUpToDate = upToDate;
            } else {
                skippedOrFailed.Add("Mod impact assessment skipped - it requires a successful game compare.");
            }

            if (baseline.Record.ModsStage == StageStatus.Completed) {
                var oldMods = JsonUtil.ReadFile<ModManifest>(baseline.Record.ModsManifestPath!);
                var (added, removed) = ModsCompareEngine.CompareInventory(oldMods, newMods, cancellationToken);
                report.ModInventoryDelta = new ModInventoryDelta { Added = added, Removed = removed };
            } else {
                warnings.Add("Mod inventory delta unavailable - the baseline's mods capture did not complete.");
            }
        } else {
            skippedOrFailed.Add("Mods compare skipped - this run's mods capture did not complete.");
        }

        HeaderCompareResult? headerDiff = null;
        if (baseline.Record.HeaderStage == StageStatus.Completed && record.HeaderStage == StageStatus.Completed) {
            var oldHeader = JsonUtil.ReadFile<HeaderManifest>(baseline.Record.HeaderManifestPath!);
            var newHeader = JsonUtil.ReadFile<HeaderManifest>(record.HeaderManifestPath!);
            headerDiff = HeaderCompareEngine.Compare(oldHeader, newHeader, cancellationToken: cancellationToken);
            headerDiff.OldManifestPath = baseline.Record.HeaderManifestPath!;
            headerDiff.NewManifestPath = record.HeaderManifestPath!;

            cancellationToken.ThrowIfCancellationRequested();
            var headerComparePath = Path.Combine(runDirectory, "header-comparison.json");
            JsonUtil.WriteFile(headerComparePath, headerDiff);
            AtomicFile.WriteAllText(Path.Combine(runDirectory, "header-comparison.md"), HeaderCompareEngine.RenderMarkdown(headerDiff));

            report.HeaderComparePath = headerComparePath;
            report.HeaderCxxChangedTypes = headerDiff.CxxChangedTypes.Count;
            report.HeaderUhtChangedTypes = headerDiff.UhtChangedTypes.Count;

            // Timestamps alone can't prove a UE4SS dump was actually regenerated against the exact game
            // version just captured - this feature doesn't generate dumps itself (CODE-18's own requirement),
            // so it can't detect staleness beyond restating this limitation every time headers are compared.
            warnings.Add("Header timestamps cannot prove the UE4SS dump was regenerated against the game version just captured - always regenerate it immediately before each capture.");
        } else if (options.HeaderFolder != null) {
            var reason = record.HeaderStage != StageStatus.Completed
                ? "this run's header capture did not complete"
                : "the baseline has no header capture (headers may not have been enabled when it was created)";
            skippedOrFailed.Add($"Header compare skipped - {reason}.");
        }

        report.Warnings = warnings;
        report.SkippedOrFailedStages = skippedOrFailed;

        cancellationToken.ThrowIfCancellationRequested();
        var reportPath = Path.Combine(runDirectory, "report.json");
        JsonUtil.WriteFile(reportPath, report);
        AtomicFile.WriteAllText(Path.Combine(runDirectory, "report.md"), RenderReportMarkdown(report));
        AtomicFile.WriteAllText(Path.Combine(runDirectory, "log.txt"), string.Join(Environment.NewLine, logLines));

        return report;
    }

    private static string RenderReportMarkdown(QuickActionReport report) {
        var sb = new StringBuilder();
        sb.AppendLine("# Update audit report");
        sb.AppendLine();
        sb.AppendLine($"Generated: {report.GeneratedAt}");
        sb.AppendLine($"Baseline run: `{report.BeforeRunId}`  |  This run: `{report.AfterRunId}`");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();

        if (report.GameComparePath != null)
            sb.AppendLine($"- **Game**: {report.GameAdded} added, {report.GameRemoved} removed, {report.GameChanged} changed, " +
                $"{report.GameUnchanged} unchanged, {report.GameUncertain} uncertain. See `game-comparison.md`.");
        if (report.ModsComparePath != null)
            sb.AppendLine($"- **Mods**: {report.ModsNeedingReview} potentially affected (review required), " +
                $"{report.ModsNeedingUncertainReview} need review, {report.ModsUpToDate} unaffected. See `mods-report.md`.");
        if (report.ModInventoryDelta.Added.Count > 0 || report.ModInventoryDelta.Removed.Count > 0) {
            sb.AppendLine($"- **Mod inventory changed since baseline**: {report.ModInventoryDelta.Added.Count} added, " +
                $"{report.ModInventoryDelta.Removed.Count} removed.");
            foreach (var added in report.ModInventoryDelta.Added)
                sb.AppendLine($"  - Added: {MarkdownEscape.Cell(added)}");
            foreach (var removed in report.ModInventoryDelta.Removed)
                sb.AppendLine($"  - Removed: {MarkdownEscape.Cell(removed)} (no longer installed - not part of the mod impact assessment above)");
        }
        if (report.HeaderComparePath != null)
            sb.AppendLine($"- **Headers**: {report.HeaderCxxChangedTypes} CXX type(s) changed, " +
                $"{report.HeaderUhtChangedTypes} UHT type(s) changed. See `header-comparison.md`.");

        if (report.Warnings.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            foreach (var warning in report.Warnings)
                sb.AppendLine($"- {MarkdownEscape.Cell(warning)}");
        }

        if (report.SkippedOrFailedStages.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("## Skipped or failed");
            sb.AppendLine();
            foreach (var reason in report.SkippedOrFailedStages)
                sb.AppendLine($"- {MarkdownEscape.Cell(reason)}");
        }

        return sb.ToString();
    }
}
