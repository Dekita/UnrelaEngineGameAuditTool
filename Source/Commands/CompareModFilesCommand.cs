using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

public static class CompareModFilesCommand {
    public static int Run(ArgMap flags, CancellationToken cancellationToken = default) {
        flags.EnsureKnownKeys("diff", "mods", "out", "md");

        var diffPath = flags.RequireOne("diff");
        var modsPath = flags.RequireOne("mods");
        var outPath = flags.OneOrDefault("out");
        var mdPath = flags.OneOrDefault("md");

        PathCollisionGuard.CheckNoCollisions(
            inputs: [("--diff", diffPath), ("--mods", modsPath)],
            outputs: [("--out", outPath), ("--md", mdPath)]);

        var diff = JsonUtil.ReadFile<GameCompareResult>(diffPath);
        var modManifest = JsonUtil.ReadFile<ModManifest>(modsPath);

        if (diff.Uncertain.Count > 0)
            Console.WriteLine($"WARN: the game comparison has {diff.Uncertain.Count} uncertain asset(s) (scan errors on one or both sides) - mods referencing them are flagged for review below, not marked confidently unaffected.");

        var report = ModsCompareEngine.Compare(diff, modManifest, cancellationToken);
        report.GameComparePath = Path.GetFullPath(diffPath);
        report.ModsManifestPath = Path.GetFullPath(modsPath);
        var (needsUpdate, needsReview, upToDate) = ModsCompareEngine.Summarize(report);

        if (!string.IsNullOrEmpty(outPath)) {
            cancellationToken.ThrowIfCancellationRequested();
            JsonUtil.WriteFile(outPath, report);
        }

        Console.WriteLine();
        Console.WriteLine("== Mods potentially affected - review required ==");
        Console.WriteLine("(overrides an asset the update changed or removed - not proof it's actually broken)");
        Console.WriteLine();
        if (needsUpdate.Count == 0) {
            Console.WriteLine("None confirmed.");
        } else {
            foreach (var (modName, entry) in needsUpdate) {
                Console.WriteLine($"{modName}  ({entry.AffectedAssets.Count} affected asset(s))");
                foreach (var asset in entry.AffectedAssets)
                    Console.WriteLine($"  - {asset}");
            }
        }

        if (needsReview.Count > 0) {
            Console.WriteLine();
            Console.WriteLine("== Mods that need review (scan couldn't confirm, or a new collision was found) ==");
            Console.WriteLine();
            foreach (var (modName, entry) in needsReview) {
                Console.WriteLine($"{modName}  ({entry.UncertainAssets.Count} uncertain asset(s), {entry.CollidingAssets.Count} new collision(s))");
                foreach (var asset in entry.UncertainAssets)
                    Console.WriteLine($"  - uncertain: {asset}");
                foreach (var asset in entry.CollidingAssets)
                    Console.WriteLine($"  - colliding with newly added: {asset}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {needsUpdate.Count} mod(s) potentially affected (review required), {needsReview.Count} need review, {upToDate} unaffected.");

        if (!string.IsNullOrEmpty(mdPath)) {
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFile.WriteAllText(mdPath, ModsCompareEngine.RenderMarkdown(report));
            Console.WriteLine($"Wrote Markdown report to {Path.GetFullPath(mdPath)}");
        }

        if (!string.IsNullOrEmpty(outPath))
            Console.WriteLine($"Wrote JSON report to {Path.GetFullPath(outPath)}");

        return 0;
    }
}
