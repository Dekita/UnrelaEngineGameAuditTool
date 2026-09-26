using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

/// <summary>Shared flag parsing for before-update/after-update - both take the same inputs (the union of what
/// hash-game-files/hash-mod-files/hash-header-files each need, plus --out for the run/baseline root).</summary>
internal static class QuickActionOptionsArg {
    public static readonly string[] KnownKeys = [
        "paks", "mods", "ue-version", "aes", "aes-file", "scope-rules", "ue4ss", "out", "max-parallelism",
    ];

    public static QuickActionOptions Load(ArgMap flags) {
        var outputRoot = flags.RequireOne("out");
        Directory.CreateDirectory(outputRoot);

        return new QuickActionOptions {
            PaksFolder = Path.GetFullPath(flags.RequireOne("paks")),
            // --mods may repeat (CODE-17 multi-root scanning) - raw values, unresolved; ModsHasher/
            // ModRootParsing resolves each line to a full path.
            ModsFolder = string.Join('\n', flags.RequireMany("mods")),
            UeVersion = flags.RequireOne("ue-version"),
            AesKeys = AesKeyParsing.ParseAesKeys(flags.Many("aes")),
            AesFile = flags.OneOrDefault("aes-file"),
            ScopeRules = ScopeRulesArg.Load(flags),
            HeaderFolder = flags.OneOrDefault("ue4ss") is { } ue4ss ? Path.GetFullPath(ue4ss) : null,
            OutputRoot = Path.GetFullPath(outputRoot),
            MaxDegreeOfParallelism = HashGameFilesCommand.ParsePositiveParallelism(flags.OneOrDefault("max-parallelism")),
        };
    }
}
