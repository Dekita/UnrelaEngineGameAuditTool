using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

public static class CompareGameFilesCommand {
    public static int Run(ArgMap flags, CancellationToken cancellationToken = default) {
        flags.EnsureKnownKeys("old", "new", "out", "md");

        var oldPath = flags.RequireOne("old");
        var newPath = flags.RequireOne("new");
        var outPath = flags.RequireOne("out");
        var mdPath = flags.OneOrDefault("md");

        PathCollisionGuard.CheckNoCollisions(
            inputs: [("--old", oldPath), ("--new", newPath)],
            outputs: [("--out", outPath), ("--md", mdPath)]);

        if (string.Equals(Path.GetFullPath(oldPath), Path.GetFullPath(newPath), StringComparison.OrdinalIgnoreCase))
            Console.WriteLine("WARN: --old and --new are the same file - this will compare it against itself.");

        var oldManifest = JsonUtil.ReadFile<GameManifest>(oldPath);
        var newManifest = JsonUtil.ReadFile<GameManifest>(newPath);

        var result = GameCompareEngine.Compare(oldManifest, newManifest,
            onLog: Console.WriteLine, cancellationToken: cancellationToken);
        result.OldManifestPath = Path.GetFullPath(oldPath);
        result.NewManifestPath = Path.GetFullPath(newPath);
        result.OldUeVersion = oldManifest.UeVersion;
        result.NewUeVersion = newManifest.UeVersion;

        cancellationToken.ThrowIfCancellationRequested();
        JsonUtil.WriteFile(outPath, result);

        Console.WriteLine($"Added:     {result.Added.Count}");
        Console.WriteLine($"Removed:   {result.Removed.Count}");
        Console.WriteLine($"Changed:   {result.Changed.Count}");
        Console.WriteLine($"Unchanged: {result.UnchangedCount}");
        Console.WriteLine($"Uncertain: {result.Uncertain.Count}");
        Console.WriteLine($"Wrote comparison to {Path.GetFullPath(outPath)}");

        if (!string.IsNullOrWhiteSpace(mdPath)) {
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFile.WriteAllText(mdPath, GameCompareEngine.RenderMarkdown(result));
            Console.WriteLine($"Wrote Markdown report to {Path.GetFullPath(mdPath)}");
        }

        return 0;
    }
}
