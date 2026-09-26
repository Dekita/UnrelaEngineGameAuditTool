using System.Text;

namespace DekUnrealGameAudit.Core;

/// <summary>Cross-references a game comparison (from Compare Game Files) against a mods manifest (from Hash
/// Mod Files) to report which mods are affected - not a diff between two mod snapshots, since there's only
/// ever one mods manifest involved. Shared by the CLI command and the GUI.</summary>
public static class ModsCompareEngine {
    public static ModsCompareReport Compare(GameCompareResult diff, ModManifest modManifest,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        var touched = new HashSet<string>(diff.Changed.Select(c => c.Path), StringComparer.OrdinalIgnoreCase);
        touched.UnionWith(diff.Removed);
        var uncertain = new HashSet<string>(diff.Uncertain.Select(u => u.Path), StringComparer.OrdinalIgnoreCase);
        // A path here means the update shipped a brand-new official asset there - if a mod also has an asset
        // at that exact path, the mod (loaded after game content) now silently shadows it. Not the same kind
        // of risk as `touched` (a stale override of something that changed/vanished): the mod's own override
        // target never changed, but it's now an unintended collision with new content (INV-03).
        var added = new HashSet<string>(diff.Added, StringComparer.OrdinalIgnoreCase);

        var report = new ModsCompareReport { GeneratedAt = DateTime.UtcNow.ToString("o") };

        foreach (var (modName, modInfo) in modManifest.Mods) {
            cancellationToken.ThrowIfCancellationRequested();
            var affected = modInfo.Assets.Where(touched.Contains).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            // Kept separate from `affected` - a mod referencing an uncertain asset isn't confirmed changed
            // (so it shouldn't inflate NeedsUpdate/AffectedAssets), but it also isn't confidently unaffected
            // just because it's absent from `touched` - the scan simply couldn't tell this time.
            var uncertainAssets = modInfo.Assets.Where(uncertain.Contains).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            var collidingAssets = modInfo.Assets.Where(added.Contains).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            report.Mods[modName] = new ModsCompareEntry {
                AffectedAssets = affected,
                NeedsUpdate = affected.Count > 0,
                UncertainAssets = uncertainAssets,
                CollidingAssets = collidingAssets,
                HasNewCollision = collidingAssets.Count > 0,
                // A new collision is worth a second look the same way scan-uncertainty is - not confirmed
                // affected, but not confidently unaffected either. Folded into the same NeedsReview signal
                // (rather than a 4th top-level bucket) so every existing caller of Summarize's counts picks
                // this up automatically.
                NeedsReview = uncertainAssets.Count > 0 || collidingAssets.Count > 0,
            };
        }

        return report;
    }

    /// <summary>Diffs which mod containers exist between two mod manifests (keyed by root-relative path since
    /// CODE-17) - CODE-18's "report installed/removed containers versus the baseline inventory." Distinct from
    /// <see cref="Compare"/>: this is about the mod *collection* changing (a mod was added or removed), not
    /// about a game update affecting a mod's content - a removed mod is reported here, not silently treated as
    /// still-installed-and-unaffected by <see cref="Compare"/> just because it's absent from the new manifest.</summary>
    public static (List<string> Added, List<string> Removed) CompareInventory(ModManifest before, ModManifest after,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        var beforeKeys = new HashSet<string>(before.Mods.Keys, StringComparer.OrdinalIgnoreCase);
        var afterKeys = new HashSet<string>(after.Mods.Keys, StringComparer.OrdinalIgnoreCase);

        var added = afterKeys.Except(beforeKeys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        var removed = beforeKeys.Except(afterKeys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        return (added, removed);
    }

    /// <summary>Mods sorted alphabetically that are potentially affected (reference a changed/removed asset -
    /// review required, not a certainty), mods that aren't confirmed affected but reference at least one
    /// uncertain asset (and so aren't confidently unaffected either), and a count of the rest.</summary>
    public static (List<KeyValuePair<string, ModsCompareEntry>> NeedsUpdate, List<KeyValuePair<string, ModsCompareEntry>> NeedsReview, int UpToDate) Summarize(ModsCompareReport report) {
        var needsUpdate = report.Mods.Where(m => m.Value.NeedsUpdate).OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToList();
        var needsReview = report.Mods.Where(m => !m.Value.NeedsUpdate && m.Value.NeedsReview).OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).ToList();
        return (needsUpdate, needsReview, report.Mods.Count - needsUpdate.Count - needsReview.Count);
    }

    public static string RenderMarkdown(ModsCompareReport report) {
        var (needsUpdate, needsReview, upToDate) = Summarize(report);

        var sb = new StringBuilder();
        sb.AppendLine("# Mod update check");
        sb.AppendLine();
        sb.AppendLine($"Generated: {report.GeneratedAt}");
        sb.AppendLine();
        sb.AppendLine($"**{needsUpdate.Count}** mod(s) potentially affected - review required, " +
            $"**{needsReview.Count}** need review (scan couldn't confirm, or a new collision was found), " +
            $"**{upToDate}** unaffected.");
        sb.AppendLine();

        if (needsUpdate.Count > 0) {
            sb.AppendLine("## ⚠ Potentially affected - review required");
            sb.AppendLine();
            sb.AppendLine("Each of these mods overrides an asset the game update changed or removed - that's a");
            sb.AppendLine("strong signal, not proof the mod is actually broken or that republishing it is");
            sb.AppendLine("required. Check what changed before assuming either way.");
            sb.AppendLine();
            sb.AppendLine("| Mod | Affected assets | Also uncertain | Also colliding |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var (modName, entry) in needsUpdate) {
                var assetList = string.Join("<br>", entry.AffectedAssets.Select(MarkdownEscape.Cell));
                var uncertainNote = entry.UncertainAssets.Count > 0 ? $"{entry.UncertainAssets.Count} asset(s)" : "";
                var collidingNote = entry.CollidingAssets.Count > 0 ? $"{entry.CollidingAssets.Count} asset(s)" : "";
                sb.AppendLine($"| {MarkdownEscape.Cell(modName)} | {assetList} | {uncertainNote} | {collidingNote} |");
            }
            sb.AppendLine();
        }

        if (needsReview.Count > 0) {
            sb.AppendLine("## ⚠ Need review - scan couldn't confirm these are unaffected, or a new collision was found");
            sb.AppendLine();
            sb.AppendLine("Not confirmed changed, but each either references an asset the game scan couldn't " +
                "read on one or both sides (see the comparison's Uncertain section), or has an asset at a " +
                "path the update newly added - the mod didn't override anything there before, but now silently " +
                "shadows new official content.");
            sb.AppendLine();
            sb.AppendLine("| Mod | Uncertain assets | New collisions (path newly added by the update) |");
            sb.AppendLine("|---|---|---|");
            foreach (var (modName, entry) in needsReview) {
                var uncertainList = string.Join("<br>", entry.UncertainAssets.Select(MarkdownEscape.Cell));
                var collidingList = string.Join("<br>", entry.CollidingAssets.Select(MarkdownEscape.Cell));
                sb.AppendLine($"| {MarkdownEscape.Cell(modName)} | {uncertainList} | {collidingList} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
