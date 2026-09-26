using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for the parts of CODE-18's Before orchestration testable without real pak
/// content (an already-cancelled token short-circuits before any CUE4Parse work, matching this project's
/// existing boundary that CUE4Parse-touching code itself is verified live, not unit-tested).</summary>
public class BeforeUpdateOrchestratorTests : IDisposable {
    private readonly string _outputRoot = Directory.CreateTempSubdirectory("before-update-test-").FullName;

    public void Dispose() => Directory.Delete(_outputRoot, recursive: true);

    [Fact]
    public void Run_WithAnAlreadyCancelledToken_RecordsTheInterruptedRunAndThrows_NoBaselineActivated() {
        var options = new QuickActionOptions {
            PaksFolder = "unused - never reached",
            ModsFolder = "unused - never reached",
            UeVersion = "GAME_UE5_3",
            OutputRoot = _outputRoot,
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            BeforeUpdateOrchestrator.Run(options, cancellationToken: cts.Token));

        var runDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(_outputRoot, "runs")));
        var record = RunDirectoryStore.LoadRunRecord(runDirectory);

        Assert.Equal(StageStatus.Failed, record.GameStage);
        Assert.Equal(StageStatus.Skipped, record.ModsStage);
        Assert.Equal(StageStatus.Skipped, record.HeaderStage);
        Assert.Contains(record.Notes, n => n.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
        Assert.Null(RunDirectoryStore.TryLoadActiveBaseline(_outputRoot));
    }
}
