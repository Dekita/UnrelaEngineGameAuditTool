using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

public static class BeforeUpdateCommand {
    public static int Run(ArgMap flags, CancellationToken cancellationToken = default) {
        flags.EnsureKnownKeys(QuickActionOptionsArg.KnownKeys);
        var options = QuickActionOptionsArg.Load(flags);

        Console.WriteLine($"Capturing baseline under: {options.OutputRoot}");

        var record = BeforeUpdateOrchestrator.Run(options, onLog: Console.WriteLine,
            cancellationToken: cancellationToken);

        var runDirectory = Path.Combine(options.OutputRoot, "runs", record.RunId);
        Console.WriteLine();
        Console.WriteLine($"Run directory: {runDirectory}");

        var headerOk = options.HeaderFolder == null || record.HeaderStage == StageStatus.Completed;
        var baselineActivated = record.GameStage == StageStatus.Completed && record.ModsStage == StageStatus.Completed && headerOk;
        if (!baselineActivated) {
            Console.WriteLine("Baseline was NOT activated - see the notes above for which stage(s) didn't complete.");
            return 1;
        }

        return 0;
    }
}
