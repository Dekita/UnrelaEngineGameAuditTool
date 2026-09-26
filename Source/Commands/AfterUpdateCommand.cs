using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

public static class AfterUpdateCommand {
    public static int Run(ArgMap flags, CancellationToken cancellationToken = default) {
        flags.EnsureKnownKeys(QuickActionOptionsArg.KnownKeys);
        var options = QuickActionOptionsArg.Load(flags);

        Console.WriteLine($"Auditing update against the active baseline under: {options.OutputRoot}");

        var report = AfterUpdateOrchestrator.Run(options, onLog: Console.WriteLine,
            cancellationToken: cancellationToken);

        var runDirectory = Path.Combine(options.OutputRoot, "runs", report.AfterRunId);
        Console.WriteLine();
        Console.WriteLine($"Report written to: {Path.Combine(runDirectory, "report.md")}");

        if (report.SkippedOrFailedStages.Count > 0) {
            Console.WriteLine("Some comparisons were skipped or failed - see report.md for full details:");
            foreach (var reason in report.SkippedOrFailedStages)
                Console.WriteLine($"  - {reason}");
        }

        return report.SkippedOrFailedStages.Count > 0 ? 1 : 0;
    }
}
