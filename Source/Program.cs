using DekUnrealGameAudit.Commands;

if (args.Length == 0) {
    PrintUsage();
    return 1;
}

var verb = args[0];
using var cancellation = new CancellationTokenSource();
ConsoleCancelEventHandler onCancel = (_, e) => {
    e.Cancel = true;
    Console.WriteLine("Cancelling...");
    cancellation.Cancel();
};
Console.CancelKeyPress += onCancel;

try {
    var flags = ArgMap.Parse(args.Skip(1));
    return verb switch {
        "hash-game-files" => HashGameFilesCommand.Run(flags, cancellation.Token),
        "hash-mod-files" => HashModFilesCommand.Run(flags, cancellation.Token),
        "compare-game-files" => CompareGameFilesCommand.Run(flags, cancellation.Token),
        "compare-mod-files" => CompareModFilesCommand.Run(flags, cancellation.Token),
        "hash-header-files" => HashHeaderFilesCommand.Run(flags, cancellation.Token),
        "compare-header-files" => CompareHeaderFilesCommand.Run(flags, cancellation.Token),
        "before-update" => BeforeUpdateCommand.Run(flags, cancellation.Token),
        "after-update" => AfterUpdateCommand.Run(flags, cancellation.Token),
        "help" or "-h" or "--help" => Help(),
        _ => Unknown(verb)
    };
} catch (OperationCanceledException) {
    Console.Error.WriteLine("Cancelled - no pending output was published.");
    return 130;
} catch (Exception ex) {
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
} finally {
    Console.CancelKeyPress -= onCancel;
}

int Help() {
    PrintUsage();
    return 0;
}

int Unknown(string v) {
    Console.Error.WriteLine($"Unknown command: {v}");
    PrintUsage();
    return 1;
}

static void PrintUsage() {
    Console.WriteLine("""
        DekUnrealGameAudit - hash Unreal Engine pak/utoc+ucas assets and compare them across game updates.

        Usage:
          DekUnrealGameAudit hash-game-files     --paks <folder> --ue-version <EGame name> [--aes GUID:HEXKEY]... [--aes-file keys.txt] [--version-tag text] [--out manifest.json] [--max-parallelism N]
          DekUnrealGameAudit compare-game-files  --old old-manifest.json --new new-manifest.json --out comparison.json [--md report.md]
          DekUnrealGameAudit hash-mod-files      --mods <folder> --ue-version <EGame name> [--aes GUID:HEXKEY]... [--aes-file keys.txt] [--out mods-manifest.json]
          DekUnrealGameAudit compare-mod-files   --diff comparison.json --mods mods-manifest.json [--out report.json] [--md report.md]
          DekUnrealGameAudit hash-header-files   --ue4ss <ue4ss folder> [--out header-manifest.json]
          DekUnrealGameAudit compare-header-files --old old-headers.json --new new-headers.json --out header-comparison.json [--md report.md]
          DekUnrealGameAudit before-update        --paks <folder> --mods <folder> --ue-version <EGame name> --out <run folder> [--aes GUID:HEXKEY]... [--aes-file keys.txt] [--scope-rules rules.json] [--ue4ss <ue4ss folder>] [--max-parallelism N]
          DekUnrealGameAudit after-update         --paks <folder> --mods <folder> --ue-version <EGame name> --out <run folder> [--aes GUID:HEXKEY]... [--aes-file keys.txt] [--scope-rules rules.json] [--ue4ss <ue4ss folder>] [--max-parallelism N]

        --ue-version must be an exact EGame enum member name from CUE4Parse, e.g. GAME_UE5_3.
        See: https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse/UE4/Versions/EGame.cs

        hash-game-files's --out is optional: when omitted, the manifest is auto-named from the game's
        own detected version (or --version-tag, which overrides detection), today's date, and a short
        random suffix so two runs never collide, e.g. game-manifest-1.3.0-20260919-a1b2c3.json.
        hash-mod-files' and hash-header-files' --out are optional the same way.

        Any operation can be interrupted with Ctrl+C and exits without publishing its pending output.
        Hashing finishes reads already in flight rather than killing the process mid-read. --max-parallelism caps how
        many assets are hashed concurrently (default: one per CPU core) - lower it if a spinning
        disk is thrashing under too many concurrent reads.

        hash-header-files/compare-header-files work with UE4SS's own dump output (the folder next to
        its DLL, e.g. <Game>\Binaries\Win64\ue4ss) - specifically its CXXHeaderDump (exact memory
        offsets/sizes per field, for detecting native-mod-breaking layout changes) and UHTHeaderDump
        (reconstructed source with UPROPERTY/UFUNCTION metadata, for semantic changes) subfolders.
        Either or both may be present; --out auto-names like hash-game-files' does when omitted.

        compare-mod-files (formerly "check-mods") doesn't compare two mod manifests against each other -
        it cross-references your one mods manifest against a compare-game-files result to report which
        mods are actually affected by the update.

        before-update/after-update automate the whole hash-game-files + hash-mod-files (+ hash-header-files
        when --ue4ss is given) + compare + report workflow above, into a single --out run folder:
        before-update captures a baseline (activated only once every required stage - game and mods, plus
        headers when --ue4ss is given - actually completes; a failed run never disturbs a previous good
        baseline); after-update rescans, finds that baseline with no manual manifest picking, compares
        everything possible given what succeeded on both sides, and writes game-comparison.*, mods-report.*,
        header-comparison.* (when applicable), a combined report.md/report.json and a full log.txt into its
        own new run subfolder under --out. after-update fails (nonzero exit) with no active baseline for
        that --out folder - run before-update there first.
        """);
}
