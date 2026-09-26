using System.Text;

namespace DekUnrealGameAudit.Core;

/// <summary>Compares two game manifests (from Hash Game Files), shared by the CLI command and the GUI.</summary>
public static class GameCompareEngine {
    public static GameCompareResult Compare(GameManifest oldManifest, GameManifest newManifest,
        Action<string>? onLog = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(oldManifest.UeVersion, newManifest.UeVersion, StringComparison.Ordinal))
            onLog?.Invoke($"WARN: the two manifests were hashed with different UE versions " +
                $"('{oldManifest.UeVersion}' vs '{newManifest.UeVersion}') - make sure these are really the " +
                "same game before trusting this comparison.");

        if (oldManifest.HashSchemeVersion != newManifest.HashSchemeVersion)
            onLog?.Invoke($"WARN: the two manifests were hashed with different internal hash schemes " +
                $"(v{oldManifest.HashSchemeVersion} vs v{newManifest.HashSchemeVersion}) - every asset may " +
                "show as Changed even though nothing really changed. Re-hash both sides with the same tool " +
                "version before trusting this comparison.");

        // A path here means "this scan tried to read this asset and failed" - it says nothing about whether
        // the asset actually exists. Cross-referencing both sides' errors against the other side's Assets is
        // what keeps a failed read from being misreported as a confident Added/Removed below.
        var oldErrors = oldManifest.Errors.ToDictionary(e => e.Path, e => e.Message, StringComparer.OrdinalIgnoreCase);
        var newErrors = newManifest.Errors.ToDictionary(e => e.Path, e => e.Message, StringComparer.OrdinalIgnoreCase);

        var result = new GameCompareResult { GeneratedAt = DateTime.UtcNow.ToString("o") };

        foreach (var (path, newEntry) in newManifest.Assets) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!oldManifest.Assets.TryGetValue(path, out var oldEntry)) {
                if (oldErrors.TryGetValue(path, out var oldError)) {
                    result.Uncertain.Add(new UncertainAsset { Path = path,
                        Reason = $"unreadable when the old manifest was hashed ({oldError}) - may have already existed; not confidently new" });
                } else {
                    result.Added.Add(path);
                }
            } else if (!string.Equals(oldEntry.Hash, newEntry.Hash, StringComparison.OrdinalIgnoreCase)) {
                result.Changed.Add(new ChangedAsset { Path = path, OldHash = oldEntry.Hash, NewHash = newEntry.Hash });
            } else {
                result.UnchangedCount++;
            }
        }

        foreach (var path in oldManifest.Assets.Keys) {
            cancellationToken.ThrowIfCancellationRequested();
            if (newManifest.Assets.ContainsKey(path))
                continue;
            if (newErrors.TryGetValue(path, out var newError)) {
                result.Uncertain.Add(new UncertainAsset { Path = path,
                    Reason = $"unreadable in this scan ({newError}) - may still exist; not confidently removed" });
            } else {
                result.Removed.Add(path);
            }
        }

        // Failed in both scans, so it never appears in either side's Assets - without this, it would be
        // silently invisible to the whole comparison instead of surfaced as "still unknown".
        foreach (var (path, oldError) in oldErrors) {
            cancellationToken.ThrowIfCancellationRequested();
            if (newErrors.TryGetValue(path, out var newError))
                result.Uncertain.Add(new UncertainAsset { Path = path, Reason = $"unreadable in both scans (old: {oldError}; new: {newError}) - status unknown" });
        }

        result.Added.Sort(StringComparer.OrdinalIgnoreCase);
        result.Removed.Sort(StringComparer.OrdinalIgnoreCase);
        result.Changed.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
        result.Uncertain.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        if (result.Uncertain.Count > 0)
            onLog?.Invoke($"WARN: {result.Uncertain.Count} asset(s) couldn't be confidently classified because of scan errors on one or both sides - see the Uncertain section of this comparison before trusting it as complete.");

        return result;
    }

    public static string RenderMarkdown(GameCompareResult result) {
        var sb = new StringBuilder();
        sb.AppendLine("# Game asset comparison");
        sb.AppendLine();
        sb.AppendLine($"Generated: {result.GeneratedAt}");
        sb.AppendLine();
        sb.AppendLine($"**Added:** {result.Added.Count}   **Removed:** {result.Removed.Count}   " +
            $"**Changed:** {result.Changed.Count}   **Unchanged:** {result.UnchangedCount}   " +
            $"**Uncertain:** {result.Uncertain.Count}");
        sb.AppendLine();

        if (result.Uncertain.Count > 0) {
            sb.AppendLine($"## ⚠ Uncertain ({result.Uncertain.Count}) - review before trusting this comparison");
            sb.AppendLine();
            sb.AppendLine("A scan error stands in for these paths on one or both sides, so they could not be");
            sb.AppendLine("confidently classified as Added, Removed, Changed or Unchanged.");
            sb.AppendLine();
            sb.AppendLine("| Path | Reason |");
            sb.AppendLine("|---|---|");
            foreach (var u in result.Uncertain) sb.AppendLine($"| {MarkdownEscape.Cell(u.Path)} | {MarkdownEscape.Cell(u.Reason)} |");
            sb.AppendLine();
        }

        if (result.Added.Count > 0) {
            sb.AppendLine($"## Added ({result.Added.Count})");
            sb.AppendLine();
            foreach (var path in result.Added) sb.AppendLine($"- {MarkdownEscape.Cell(path)}");
            sb.AppendLine();
        }

        if (result.Removed.Count > 0) {
            sb.AppendLine($"## Removed ({result.Removed.Count})");
            sb.AppendLine();
            foreach (var path in result.Removed) sb.AppendLine($"- {MarkdownEscape.Cell(path)}");
            sb.AppendLine();
        }

        if (result.Changed.Count > 0) {
            sb.AppendLine($"## Changed ({result.Changed.Count})");
            sb.AppendLine();
            sb.AppendLine("| Path | Old hash | New hash |");
            sb.AppendLine("|---|---|---|");
            foreach (var c in result.Changed) sb.AppendLine($"| {MarkdownEscape.Cell(c.Path)} | {MarkdownEscape.Cell(c.OldHash)} | {MarkdownEscape.Cell(c.NewHash)} |");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
