namespace DekUnrealGameAudit.Core;

/// <summary>Persists Before/After run data (CODE-18) under a caller-chosen output root:
/// <c>outputRoot/runs/&lt;timestamp&gt;-&lt;suffix&gt;/run-record.json</c> for each run, plus a single
/// <c>outputRoot/active-baseline.json</c> pointer recording which run directory is the current baseline. This
/// is scoped to the output folder itself rather than any "profile identity" - there is no stable profile ID
/// anywhere in the codebase today (checked: ProfileStore keys purely by display-name string), and the output
/// folder is already a first-class concept both the CLI and a future GUI caller have.</summary>
public static class RunDirectoryStore {
    private const string RunsFolderName = "runs";
    private const string RunRecordFileName = "run-record.json";
    private const string ActiveBaselineFileName = "active-baseline.json";

    public static string AllocateRunDirectory(string outputRoot) {
        var runsRoot = Path.Combine(outputRoot, RunsFolderName);
        Directory.CreateDirectory(runsRoot);
        return AutoFileNaming.AllocateUniqueDirectory(() =>
            Path.Combine(runsRoot, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{AutoFileNaming.RandomSuffix()}"));
    }

    public static void SaveRunRecord(string runDirectory, RunRecord record) =>
        JsonUtil.WriteFile(Path.Combine(runDirectory, RunRecordFileName), record);

    public static RunRecord LoadRunRecord(string runDirectory) =>
        JsonUtil.ReadFile<RunRecord>(Path.Combine(runDirectory, RunRecordFileName));

    private class ActiveBaselinePointer {
        public string RunDirectory { get; set; } = "";
    }

    /// <summary>Only ever called by BeforeUpdateOrchestrator after every required stage actually completed -
    /// a failed/cancelled Before run must never reach this, so the previous good baseline (if any) survives
    /// untouched.</summary>
    public static void SaveActiveBaseline(string outputRoot, string runDirectory) =>
        JsonUtil.WriteFile(Path.Combine(outputRoot, ActiveBaselineFileName), new ActiveBaselinePointer { RunDirectory = runDirectory });

    /// <summary>Returns the active baseline's run directory and its already-loaded RunRecord, or null if no
    /// baseline has ever been activated for this output root, or the recorded run directory no longer exists
    /// (e.g. manually deleted) - callers should surface either case as an actionable "run before-update first"
    /// message, not a raw file-not-found exception.</summary>
    public static (string RunDirectory, RunRecord Record)? TryLoadActiveBaseline(string outputRoot) {
        var pointerPath = Path.Combine(outputRoot, ActiveBaselineFileName);
        if (!File.Exists(pointerPath))
            return null;

        var pointer = JsonUtil.ReadFile<ActiveBaselinePointer>(pointerPath);
        if (!Directory.Exists(pointer.RunDirectory))
            return null;

        return (pointer.RunDirectory, LoadRunRecord(pointer.RunDirectory));
    }
}
