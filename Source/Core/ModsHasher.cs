using CUE4Parse.FileProvider;

namespace DekUnrealGameAudit.Core;

/// <summary>The actual hash-mod-files work, shared by the CLI command and the GUI. Only inspects file paths, not
/// content, so it doesn't need progress reporting the way GameHasher does - it's fast.</summary>
public static class ModsHasher {
    public static ModManifest Hash(ScanOptions options, Action<string>? onLog = null,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        var roots = ModRootParsing.ParseModRoots(options.TargetFolder);
        if (roots.Count == 0)
            throw new DirectoryNotFoundException("No mod root folder was given.");
        // Only disambiguate manifest keys when there's genuinely more than one root - a single root's manifest
        // stays byte-identical to before multi-root support existed.
        var multipleRoots = roots.Count > 1;

        var containers = new List<DiscoveredContainer>();
        var seenAbsolutePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++) {
            cancellationToken.ThrowIfCancellationRequested();
            var root = roots[rootIndex];
            List<DiscoveredContainer> found;
            if (options.ScopeRules != null) {
                ScopeClassifier.Validate(options.ScopeRules);
                var (classified, diagnostics) = ScopeContainerScanner.DiscoverAndClassify(root, options.ScopeRules, cancellationToken);
                foreach (var diagnostic in diagnostics)
                    onLog?.Invoke($"WARN: {diagnostic.Message}");
                found = classified.Where(c => c.Classification == ScopeAction.Mod)
                    .Select(c => new DiscoveredContainer(c.AbsolutePath, c.RootRelativePath)).ToList();
                onLog?.Invoke($"Scope rules selected {found.Count} of {classified.Count} container(s) for Mod under '{root}'.");
            } else {
                // No scope rules: the classic dedicated-mods-folder workflow, unaffected by the Game/Mod heuristic -
                // every container found under each root is a mod, exactly as before CODE-16/17.
                var (foundRaw, diagnostics) = ScopeContainerScanner.Discover(root, cancellationToken);
                foreach (var diagnostic in diagnostics)
                    onLog?.Invoke($"WARN: {diagnostic.Message}");
                found = foundRaw;
            }

            foreach (var container in found) {
                // De-dupes containers reachable via more than one configured root (e.g. one root nested inside
                // another) - this is CODE-17's "deduplicate overlapping roots" without needing to detect
                // filesystem overlap up front, since the same physical container simply resolves to the same
                // absolute path no matter which configured root found it.
                if (!seenAbsolutePaths.Add(Path.GetFullPath(container.AbsolutePath))) {
                    onLog?.Invoke($"Skipped '{container.RootRelativePath}' under '{root}' - already found via another configured root.");
                    continue;
                }
                // Two different roots can otherwise produce the same RootRelativePath (e.g. both have a
                // "MyMod/mod_p.pak") - prefix with a stable per-root index so neither silently overwrites the
                // other's entry in ModManifest.Mods.
                containers.Add(multipleRoots
                    ? container with { RootRelativePath = $"{rootIndex + 1}-{Path.GetFileName(root.TrimEnd('\\', '/'))}/{container.RootRelativePath}" }
                    : container);
            }
        }

        onLog?.Invoke($"Found {containers.Count} mod container(s) across {roots.Count} root(s). Mounting each independently...");

        var manifest = new ModManifest {
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            ModsFolder = options.TargetFolder,
            ScopeFingerprint = options.ScopeRules != null ? ScopeClassifier.ComputeFingerprint(options.ScopeRules) : null,
        };

        // CODE-17/INV-01: each container is mounted in its OWN provider instance, one at a time, rather than
        // all together into one shared provider. CUE4Parse's FileProviderDictionary keeps only one GameFile per
        // virtual path (confirmed via its actual public API - TryGetValue/Values/the indexer all resolve to a
        // single winner), so mounting every mod together would silently hide one mod's contribution wherever
        // two mods override the same asset - exactly the bug INV-01 describes. Mounting in isolation means
        // there is never a second container's file competing for the same dictionary slot, so every container's
        // own provider.Files.Values is necessarily 100% its own content, whether or not it overlaps another
        // mod's. This is deliberately different from GameHasher, which mounts Game content together in one
        // shared provider on purpose - a DLC/patch overriding base content there is the correct, desired merge.
        foreach (var container in containers) {
            cancellationToken.ThrowIfCancellationRequested();
            var containerOptions = new ScanOptions {
                TargetFolder = Path.GetDirectoryName(container.AbsolutePath)!,
                UeVersion = options.UeVersion,
                AesKeys = options.AesKeys,
                AesFile = options.AesFile,
            };

            DefaultFileProvider? provider = null;
            try {
                provider = ProviderFactory.Create(containerOptions, explicitContainerPaths: [container.AbsolutePath],
                    onLog: onLog, cancellationToken: cancellationToken);
                // Only one container is ever mounted here, so GroupContainerAssets' own container-name
                // grouping degenerates to at most one entry - its asset list is this container's own, in full.
                var grouped = AssetGrouper.GroupContainerAssets(provider.Files.Values, onLog);
                var assets = grouped.Values.SingleOrDefault() ?? new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                // Root-relative path is the identity key (CODE-17) - not bare container basename, so two
                // differently-located mods sharing a generic filename (e.g. "mod_p.pak") remain distinct
                // entries instead of colliding or only getting a best-effort warning.
                manifest.Mods[container.RootRelativePath] = new ModInfo { Assets = assets.ToList() };
            } catch (Exception ex) {
                if (ex is OperationCanceledException)
                    throw;
                // A single bad container (corrupt pak, unsupported format, etc.) is diagnosed and skipped -
                // it must not abort hashing every OTHER mod in the folder.
                onLog?.Invoke($"WARN: failed to mount '{container.RootRelativePath}': {ex.Message} - skipped.");
            } finally {
                provider?.UnloadAllVfs();
                provider?.Dispose();
            }
        }

        return manifest;
    }

    /// <summary>Auto-generated filename used when the caller doesn't specify one explicitly, mirroring
    /// GameHasher.BuildAutoFileName - mods have no equivalent of a detected game version to include.</summary>
    public static string BuildAutoFileName() => $"mods-manifest-{DateTime.UtcNow:yyyyMMdd}-{AutoFileNaming.RandomSuffix()}.json";
}
