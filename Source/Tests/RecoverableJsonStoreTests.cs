using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-14 (see IMPROVEMENT-PLAN.md): a corrupt settings/profiles file must
/// never be silently discarded, and a failed save must never be reported as having succeeded.</summary>
public class RecoverableJsonStoreTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory("recoverable-json-store-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathIn(string fileName) => Path.Combine(_dir, fileName);

    private class Sample {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }

    [Fact]
    public void Load_OfMissingFile_ReturnsDefaultWithoutError() {
        var result = RecoverableJsonStore.Load<Sample>(PathIn("missing.json"));

        Assert.Null(result);
    }

    [Fact]
    public void Load_OfValidJson_DeserializesNormally() {
        var path = PathIn("valid.json");
        File.WriteAllText(path, """{"Name":"Alpha","Count":3}""");

        var result = RecoverableJsonStore.Load<Sample>(path);

        Assert.NotNull(result);
        Assert.Equal("Alpha", result!.Name);
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Load_OfCorruptJson_BacksUpTheOriginalAndReturnsDefault() {
        var path = PathIn("corrupt.json");
        File.WriteAllText(path, "{ this is not valid json");

        var result = RecoverableJsonStore.Load<Sample>(path);

        Assert.Null(result);
        Assert.True(File.Exists(path)); // original left untouched
        var backups = Directory.GetFiles(_dir, "corrupt.json.corrupt-*.bak");
        Assert.Single(backups);
        Assert.Equal("{ this is not valid json", File.ReadAllText(backups[0]));
    }

    [Fact]
    public void Load_OfCorruptJson_InvokesOnCorruptCallback() {
        var path = PathIn("corrupt2.json");
        File.WriteAllText(path, "not json at all");

        var messages = new List<string>();
        RecoverableJsonStore.Load<Sample>(path, messages.Add);

        Assert.Single(messages);
        Assert.Contains("corrupt2.json", messages[0]);
    }

    [Fact]
    public void TrySave_ThenLoad_RoundTripsCorrectly() {
        var path = PathIn("roundtrip.json");
        var saved = RecoverableJsonStore.TrySave(path, new Sample { Name = "Beta", Count = 7 });

        Assert.True(saved);
        var loaded = RecoverableJsonStore.Load<Sample>(path);
        Assert.NotNull(loaded);
        Assert.Equal("Beta", loaded!.Name);
        Assert.Equal(7, loaded.Count);
    }

    [Fact]
    public void TrySave_ToAnUncreatableDirectory_ReturnsFalseInsteadOfThrowing() {
        // A file path as a parent "directory" can never be created - reliably reproduces a write failure
        // without needing OS-specific permission tricks.
        var blockerFile = PathIn("blocker.txt");
        File.WriteAllText(blockerFile, "x");
        var impossiblePath = Path.Combine(blockerFile, "settings.json");

        var saved = RecoverableJsonStore.TrySave(impossiblePath, new Sample { Name = "Gamma" });

        Assert.False(saved);
    }

    [Fact]
    public void TrySave_DoesNotLeaveATempFileBehindOnFailure() {
        var blockerFile = PathIn("blocker2.txt");
        File.WriteAllText(blockerFile, "x");
        var impossiblePath = Path.Combine(blockerFile, "settings.json");

        RecoverableJsonStore.TrySave(impossiblePath, new Sample { Name = "Delta" });

        // Only the original blocker file should exist in _dir - no stray .tmp files from AtomicFile.
        Assert.Equal(["blocker2.txt"], Directory.GetFiles(_dir).Select(Path.GetFileName));
    }
}
