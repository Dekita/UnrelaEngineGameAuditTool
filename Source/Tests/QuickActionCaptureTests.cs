using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

public class QuickActionCaptureTests : IDisposable {
    private readonly string _root = Directory.CreateTempSubdirectory("quick-capture-test-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void CaptureAll_GameFailure_DoesNotPreventIndependentModsAndHeaderCaptures() {
        var runDirectory = Path.Combine(_root, "run");
        Directory.CreateDirectory(runDirectory);
        var options = Options(headerFolder: "headers");

        var record = QuickActionCapture.CaptureAll(
            options, runDirectory, _ => { }, CancellationToken.None,
            captureGame: (_, _, _) => throw new InvalidOperationException("game failed"),
            captureMods: (_, _, _) => new ModManifest(),
            captureHeaders: (_, _, _) => new HeaderManifest());

        Assert.Equal(StageStatus.Failed, record.GameStage);
        Assert.Equal(StageStatus.Completed, record.ModsStage);
        Assert.Equal(StageStatus.Completed, record.HeaderStage);
        Assert.Contains(record.Notes, note => note.Contains("game failed", StringComparison.Ordinal));
        Assert.True(File.Exists(record.ModsManifestPath));
        Assert.True(File.Exists(record.HeaderManifestPath));
    }

    [Fact]
    public void CaptureAll_CancellationDuringMods_RecordsInterruptedStageAndSkipsHeader() {
        var runDirectory = Path.Combine(_root, "run-cancelled");
        Directory.CreateDirectory(runDirectory);
        using var cts = new CancellationTokenSource();
        var headerCalled = false;

        var record = QuickActionCapture.CaptureAll(
            Options(headerFolder: "headers"), runDirectory, _ => { }, cts.Token,
            captureGame: (_, _, _) => new GameManifest(),
            captureMods: (_, _, token) => {
                cts.Cancel();
                token.ThrowIfCancellationRequested();
                return new ModManifest();
            },
            captureHeaders: (_, _, _) => {
                headerCalled = true;
                return new HeaderManifest();
            });

        Assert.Equal(StageStatus.Completed, record.GameStage);
        Assert.Equal(StageStatus.Failed, record.ModsStage);
        Assert.Equal(StageStatus.Skipped, record.HeaderStage);
        Assert.False(headerCalled);
        Assert.Contains(record.Notes, note => note.Contains("Mods capture was cancelled", StringComparison.Ordinal));
        Assert.Contains(record.Notes, note => note.Contains("Header capture skipped", StringComparison.Ordinal));
    }

    private QuickActionOptions Options(string? headerFolder = null) => new() {
        PaksFolder = "paks",
        ModsFolder = "mods",
        UeVersion = "GAME_UE5_3",
        HeaderFolder = headerFolder,
        OutputRoot = _root,
    };
}
