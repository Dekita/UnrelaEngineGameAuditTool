namespace DekUnrealGameAudit.Core;

/// <summary>CODE-18's "Before update" quick action: captures a game manifest, mod inventory and (optionally)
/// a header manifest into a fresh run directory, and activates it as the profile's baseline - but only once
/// every *required* stage (Game, Mods - Header only when enabled) actually completed. A failed or cancelled
/// run never touches the active-baseline pointer, so the previous good baseline (if any) survives untouched.</summary>
public static class BeforeUpdateOrchestrator {
    public static RunRecord Run(QuickActionOptions options, Action<string>? onLog = null, CancellationToken cancellationToken = default) {
        void Log(string line) => onLog?.Invoke(line);

        var runDirectory = RunDirectoryStore.AllocateRunDirectory(options.OutputRoot);
        var record = QuickActionCapture.CaptureAll(options, runDirectory, Log, cancellationToken);
        RunDirectoryStore.SaveRunRecord(runDirectory, record);

        // CaptureAll records which stage observed cancellation so the interrupted run remains diagnosable,
        // then rethrow here so GUI/CLI callers can present Cancelled rather than a generic partial/failure.
        cancellationToken.ThrowIfCancellationRequested();

        var headerOk = options.HeaderFolder == null || record.HeaderStage == StageStatus.Completed;
        if (record.GameStage == StageStatus.Completed && record.ModsStage == StageStatus.Completed && headerOk) {
            RunDirectoryStore.SaveActiveBaseline(options.OutputRoot, runDirectory);
            Log($"Baseline activated: {runDirectory}");
        } else {
            Log("Baseline NOT activated - one or more required stages did not complete. Any previous baseline is unchanged.");
        }

        return record;
    }
}
