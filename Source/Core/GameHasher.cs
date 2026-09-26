using System.Collections.Concurrent;

namespace DekUnrealGameAudit.Core;

/// <summary>The actual hash-game-files work, shared by the CLI command and the GUI - takes plain
/// parameters/callbacks instead of ArgMap/Console so either caller can drive it.</summary>
public static class GameHasher {
    public static GameManifest Hash(
        ScanOptions options,
        Action<string>? onLog = null,
        Action<int, int>? onProgress = null,
        CancellationToken cancellationToken = default) {
        var provider = ProviderFactory.Create(options, wantedActions: [ScopeAction.Game], onLog: onLog,
            cancellationToken: cancellationToken);
        try {
            onLog?.Invoke($"Mounted {provider.Files.Count} physical entries. Grouping into logical assets...");
            var groups = AssetGrouper.GroupByLogicalAsset(provider.Files.Values);
            var ordered = AssetGrouper.OrderForSequentialRead(groups);
            onLog?.Invoke($"Found {ordered.Count} logical assets. Hashing...");

            var detectedVersion = GameVersionDetector.TryDetect(provider, onLog);

            // Deliberately leaves CUE4Parse's IsConcurrent flag untouched (default false): that mechanism
            // clones a whole new archive - reopening the underlying file from disk - on every single read,
            // which measured far slower than sequential. Instead this relies on the fact that the actual
            // hot-path reads (FArchive.ReadAt) are positional and never touch the archive's shared mutable
            // Position, so concurrent calls against the same already-open reader are safe without cloning.
            // This overlaps I/O wait time across assets instead of processing one read at a time.
            var assets = new ConcurrentDictionary<string, AssetEntry>(StringComparer.OrdinalIgnoreCase);
            var errors = new ConcurrentBag<AssetError>();
            var done = 0;

            var parallelOptions = new ParallelOptions {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism ?? -1,
            };

            try {
                Parallel.ForEach(ordered, parallelOptions, kvp => {
                    var (assetPath, parts) = (kvp.Key, kvp.Value);
                    try {
                        var (hash, size) = AssetGrouper.HashParts(parts);
                        assets[assetPath] = new AssetEntry { Hash = hash, Size = size };
                    } catch (Exception ex) {
                        errors.Add(new AssetError { Path = assetPath, Message = ex.Message });
                        onLog?.Invoke($"  WARN: failed to hash '{assetPath}': {ex.Message}");
                    }

                    var current = Interlocked.Increment(ref done);
                    onProgress?.Invoke(current, ordered.Count);
                });
            } catch (OperationCanceledException) {
                onLog?.Invoke($"Cancelled after hashing {done}/{ordered.Count} asset(s).");
                throw;
            }

            return new GameManifest {
                GeneratedAt = DateTime.UtcNow.ToString("o"),
                UeVersion = options.UeVersion,
                PaksFolder = options.TargetFolder,
                DetectedGameVersion = detectedVersion,
                Assets = new Dictionary<string, AssetEntry>(assets, StringComparer.OrdinalIgnoreCase),
                Errors = errors.ToList(),
                ScopeFingerprint = options.ScopeRules != null ? ScopeClassifier.ComputeFingerprint(options.ScopeRules) : null,
                HashSchemeVersion = AssetGrouper.CurrentHashSchemeVersion,
            };
        } finally {
            provider.UnloadAllVfs();
            provider.Dispose();
        }
    }

    /// <summary>How much of a (possibly long, user-supplied) version tag to embed in an auto-generated
    /// filename - keeps a runaway --version-tag value from producing an unreasonably long path.</summary>
    private const int MaxVersionSegmentLength = 40;

    /// <summary>Auto-generated filename used when the caller doesn't specify one explicitly, e.g.
    /// "game-manifest-1.3.0-20260919-a1b2c3.json", falling back to no version segment when none is available.
    /// The version segment is sanitized/bounded independently of <see cref="GameManifest.VersionTag"/> itself -
    /// a user-supplied --version-tag (unlike a detected one) never goes through
    /// <see cref="GameVersionDetector.Sanitize"/>, so e.g. "release/1" would otherwise create an unintended
    /// path segment. The raw tag is still recorded as-is in the manifest; only the filename is affected.</summary>
    public static string BuildAutoFileName(GameManifest manifest) {
        var datePart = DateTime.UtcNow.ToString("yyyyMMdd");
        var suffix = AutoFileNaming.RandomSuffix();
        var versionSegment = SanitizeVersionSegment(manifest.VersionTag);
        return versionSegment == null
            ? $"game-manifest-{datePart}-{suffix}.json"
            : $"game-manifest-{versionSegment}-{datePart}-{suffix}.json";
    }

    private static string? SanitizeVersionSegment(string? versionTag) {
        if (string.IsNullOrWhiteSpace(versionTag))
            return null;
        var sanitized = GameVersionDetector.Sanitize(versionTag);
        return sanitized.Length > MaxVersionSegmentLength ? sanitized[..MaxVersionSegmentLength] : sanitized;
    }
}
