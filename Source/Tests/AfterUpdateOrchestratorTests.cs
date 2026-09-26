using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for the parts of CODE-18's After orchestration testable without real pak
/// content - the active-baseline check happens before any capture stage runs, so this never touches
/// CUE4Parse, matching this project's existing boundary that CUE4Parse-touching code is verified live.</summary>
public class AfterUpdateOrchestratorTests : IDisposable {
    private readonly string _outputRoot = Directory.CreateTempSubdirectory("after-update-test-").FullName;

    public void Dispose() => Directory.Delete(_outputRoot, recursive: true);

    [Fact]
    public void Run_WithNoActiveBaseline_ThrowsAnActionableErrorBeforeAnyCapture() {
        var options = new QuickActionOptions {
            PaksFolder = "unused - never reached",
            ModsFolder = "unused - never reached",
            UeVersion = "GAME_UE5_3",
            OutputRoot = _outputRoot,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => AfterUpdateOrchestrator.Run(options));

        Assert.Contains("before-update", ex.Message);
        // Confirms this really did short-circuit before touching CUE4Parse - no run directory was created.
        Assert.False(Directory.Exists(Path.Combine(_outputRoot, "runs")));
    }
}
