using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

public static class HashModFilesCommand {
    public static int Run(ArgMap flags, CancellationToken cancellationToken = default) {
        flags.EnsureKnownKeys("mods", "ue-version", "out", "aes", "aes-file", "scope-rules");

        // --mods may repeat (same as --aes already does) for CODE-17 multi-root scanning - joined into the one
        // newline-per-root string ModsHasher/ModRootParsing expect, matching the GUI's multi-line box.
        var modsRoots = flags.RequireMany("mods");
        var ueVersion = flags.RequireOne("ue-version");
        var outPath = flags.OneOrDefault("out");
        var aesFile = flags.OneOrDefault("aes-file");
        var aesKeys = AesKeyParsing.ParseAesKeys(flags.Many("aes"));
        var scopeRules = ScopeRulesArg.Load(flags);

        if (!string.IsNullOrWhiteSpace(outPath))
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);

        var options = new ScanOptions {
            // Raw values, unresolved - ModsHasher/ModRootParsing resolves each line to a full path.
            TargetFolder = string.Join('\n', modsRoots),
            UeVersion = ueVersion,
            AesKeys = aesKeys,
            AesFile = aesFile,
            ScopeRules = scopeRules,
        };

        Console.WriteLine($"Mounting mod paks from: {options.TargetFolder}");
        var manifest = ModsHasher.Hash(options, onLog: Console.WriteLine, cancellationToken: cancellationToken);

        var finalOutPath = !string.IsNullOrWhiteSpace(outPath)
            ? outPath
            : AutoFileNaming.AllocateUniquePath(ModsHasher.BuildAutoFileName);
        cancellationToken.ThrowIfCancellationRequested();
        JsonUtil.WriteFile(finalOutPath, manifest);
        Console.WriteLine($"Wrote manifest with {manifest.Mods.Count} mod containers to {Path.GetFullPath(finalOutPath)}");
        return 0;
    }
}
