using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

public static class CompareHeaderFilesCommand {
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

        var oldManifest = JsonUtil.ReadFile<HeaderManifest>(oldPath);
        var newManifest = JsonUtil.ReadFile<HeaderManifest>(newPath);

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest,
            onLog: Console.WriteLine, cancellationToken: cancellationToken);
        result.OldManifestPath = Path.GetFullPath(oldPath);
        result.NewManifestPath = Path.GetFullPath(newPath);

        cancellationToken.ThrowIfCancellationRequested();
        JsonUtil.WriteFile(outPath, result);

        Console.WriteLine("Memory layout (CXXHeaderDump):" + (result.CxxCoverageKnown ? "" : " ⚠ coverage unknown - not a real comparison"));
        Console.WriteLine($"  Types  - Added: {result.CxxAddedTypes.Count}   Removed: {result.CxxRemovedTypes.Count}   Changed: {result.CxxChangedTypes.Count}   Unchanged: {result.CxxUnchangedTypeCount}");
        Console.WriteLine($"  Enums  - Added: {result.CxxAddedEnums.Count}   Removed: {result.CxxRemovedEnums.Count}   Changed: {result.CxxChangedEnums.Count}   Unchanged: {result.CxxUnchangedEnumCount}");
        Console.WriteLine("Source/reflection (UHTHeaderDump):" + (result.UhtCoverageKnown ? "" : " ⚠ coverage unknown - not a real comparison"));
        Console.WriteLine($"  Types  - Added: {result.UhtAddedTypes.Count}   Removed: {result.UhtRemovedTypes.Count}   Changed: {result.UhtChangedTypes.Count}   Unchanged: {result.UhtUnchangedTypeCount}");
        Console.WriteLine($"Wrote header comparison to {Path.GetFullPath(outPath)}");

        if (!string.IsNullOrWhiteSpace(mdPath)) {
            cancellationToken.ThrowIfCancellationRequested();
            AtomicFile.WriteAllText(mdPath, HeaderCompareEngine.RenderMarkdown(result));
            Console.WriteLine($"Wrote Markdown report to {Path.GetFullPath(mdPath)}");
        }

        return 0;
    }
}
