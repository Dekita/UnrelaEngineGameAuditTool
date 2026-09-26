using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

public static class HashGameFilesCommand {
    public static int Run(ArgMap flags, CancellationToken cancellationToken = default) {
        flags.EnsureKnownKeys("paks", "ue-version", "out", "version-tag", "aes", "aes-file", "max-parallelism", "scope-rules");

        var paksFolder = flags.RequireOne("paks");
        var ueVersion = flags.RequireOne("ue-version");
        var outPath = flags.OneOrDefault("out");
        var versionTag = flags.OneOrDefault("version-tag");
        var aesFile = flags.OneOrDefault("aes-file");
        var aesKeys = AesKeyParsing.ParseAesKeys(flags.Many("aes"));
        var maxParallelism = ParsePositiveParallelism(flags.OneOrDefault("max-parallelism"));
        var scopeRules = ScopeRulesArg.Load(flags);

        // Preflight the explicit output path (if any) before the potentially very long scan below, so an
        // invalid path or an inaccessible directory fails immediately instead of after mounting/hashing a
        // whole game's worth of assets.
        if (!string.IsNullOrWhiteSpace(outPath))
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);

        var options = new ScanOptions {
            TargetFolder = Path.GetFullPath(paksFolder),
            UeVersion = ueVersion,
            AesKeys = aesKeys,
            AesFile = aesFile,
            MaxDegreeOfParallelism = maxParallelism,
            ScopeRules = scopeRules,
        };

        Console.WriteLine($"Mounting paks from: {options.TargetFolder}");

        GameManifest manifest;
        try {
            manifest = GameHasher.Hash(
                options,
                onLog: Console.WriteLine,
                onProgress: (done, total) => {
                    if (done % 500 == 0 || done == total)
                        Console.WriteLine($"  Hashed {done}/{total}");
                },
                cancellationToken: cancellationToken);
        } catch (OperationCanceledException) {
            Console.WriteLine("Cancelled - no manifest was written.");
            return 130;
        }

        manifest.VersionTag = !string.IsNullOrWhiteSpace(versionTag) ? versionTag : manifest.DetectedGameVersion;
        if (manifest.DetectedGameVersion != null)
            Console.WriteLine($"Detected game version: {manifest.DetectedGameVersion}");

        var finalOutPath = !string.IsNullOrWhiteSpace(outPath)
            ? outPath
            : AutoFileNaming.AllocateUniquePath(() => GameHasher.BuildAutoFileName(manifest));
        cancellationToken.ThrowIfCancellationRequested();
        JsonUtil.WriteFile(finalOutPath, manifest);
        Console.WriteLine($"Wrote manifest with {manifest.Assets.Count} assets to {Path.GetFullPath(finalOutPath)}" +
            (manifest.Errors.Count > 0 ? $" ({manifest.Errors.Count} asset(s) failed to hash - see \"errors\" in the manifest)" : ""));
        return 0;
    }

    /// <summary>.NET's own ParallelOptions.MaxDegreeOfParallelism throws for 0 or anything below -1, but only
    /// once the (potentially slow) mount/scan is already underway - validating a positive value upfront turns
    /// that into an immediate, clear CLI error instead of a confusing runtime exception after minutes of work.</summary>
    internal static int? ParsePositiveParallelism(string? raw) {
        if (raw == null)
            return null;
        if (!int.TryParse(raw, out var value) || value < 1)
            throw new ArgumentException($"--max-parallelism must be a positive integer, got '{raw}'.");
        return value;
    }
}
