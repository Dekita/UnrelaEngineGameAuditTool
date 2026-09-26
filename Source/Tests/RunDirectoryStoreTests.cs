using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-18's run/baseline persistence - directory allocation, run-record
/// round-tripping, and the active-baseline pointer (including its "healed" behavior when the pointer exists
/// but the run directory it names has since been deleted).</summary>
public class RunDirectoryStoreTests : IDisposable {
    private readonly string _outputRoot = Directory.CreateTempSubdirectory("run-directory-store-test-").FullName;

    public void Dispose() => Directory.Delete(_outputRoot, recursive: true);

    private static RunRecord SampleRecord(string runId) => new() {
        RunId = runId,
        CreatedAt = DateTime.UtcNow.ToString("o"),
        UeVersion = "GAME_UE5_3",
        GameStage = StageStatus.Completed,
        ModsStage = StageStatus.Completed,
        HeaderStage = StageStatus.Skipped,
        GameManifestPath = "game-manifest.json",
        ModsManifestPath = "mods-manifest.json",
    };

    [Fact]
    public void AllocateRunDirectory_ReturnsANewExistingDirectoryEachTime() {
        var first = RunDirectoryStore.AllocateRunDirectory(_outputRoot);
        var second = RunDirectoryStore.AllocateRunDirectory(_outputRoot);

        Assert.True(Directory.Exists(first));
        Assert.True(Directory.Exists(second));
        Assert.NotEqual(first, second);
        Assert.StartsWith(Path.Combine(_outputRoot, "runs"), first);
    }

    [Fact]
    public void SaveAndLoadRunRecord_RoundTripsAllFields() {
        var runDir = RunDirectoryStore.AllocateRunDirectory(_outputRoot);
        var record = SampleRecord("test-run");
        record.HeaderStage = StageStatus.Failed;
        record.Notes.Add("header capture failed: no such folder");

        RunDirectoryStore.SaveRunRecord(runDir, record);
        var loaded = RunDirectoryStore.LoadRunRecord(runDir);

        Assert.Equal(record.RunId, loaded.RunId);
        Assert.Equal(StageStatus.Completed, loaded.GameStage);
        Assert.Equal(StageStatus.Completed, loaded.ModsStage);
        Assert.Equal(StageStatus.Failed, loaded.HeaderStage);
        Assert.Equal(record.Notes, loaded.Notes);
    }

    [Fact]
    public void TryLoadActiveBaseline_WithNoPointerFile_ReturnsNull() {
        Assert.Null(RunDirectoryStore.TryLoadActiveBaseline(_outputRoot));
    }

    [Fact]
    public void SaveAndTryLoadActiveBaseline_RoundTrips() {
        var runDir = RunDirectoryStore.AllocateRunDirectory(_outputRoot);
        RunDirectoryStore.SaveRunRecord(runDir, SampleRecord("baseline-run"));

        RunDirectoryStore.SaveActiveBaseline(_outputRoot, runDir);
        var loaded = RunDirectoryStore.TryLoadActiveBaseline(_outputRoot);

        Assert.NotNull(loaded);
        Assert.Equal(runDir, loaded.Value.RunDirectory);
        Assert.Equal("baseline-run", loaded.Value.Record.RunId);
    }

    [Fact]
    public void TryLoadActiveBaseline_PointingAtADeletedRunDirectory_ReturnsNullInsteadOfThrowing() {
        var runDir = RunDirectoryStore.AllocateRunDirectory(_outputRoot);
        RunDirectoryStore.SaveRunRecord(runDir, SampleRecord("vanished-run"));
        RunDirectoryStore.SaveActiveBaseline(_outputRoot, runDir);

        Directory.Delete(runDir, recursive: true);

        Assert.Null(RunDirectoryStore.TryLoadActiveBaseline(_outputRoot));
    }
}
