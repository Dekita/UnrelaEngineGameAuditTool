using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for the collision-safety fix: two hash runs with the same detected version on
/// the same day (e.g. running a now-consolidated hash script before and after an update) must never produce
/// the same auto-generated filename.</summary>
public class AutoFileNamingTests {
    [Fact]
    public void RandomSuffix_DiffersAcrossCalls() {
        Assert.NotEqual(AutoFileNaming.RandomSuffix(), AutoFileNaming.RandomSuffix());
    }

    [Fact]
    public void GameHasher_BuildAutoFileName_DiffersAcrossCallsWithSameVersion() {
        var manifest = new GameManifest { VersionTag = "1.0.0" };
        Assert.NotEqual(GameHasher.BuildAutoFileName(manifest), GameHasher.BuildAutoFileName(manifest));
    }

    [Fact]
    public void ModsHasher_BuildAutoFileName_DiffersAcrossCalls() {
        Assert.NotEqual(ModsHasher.BuildAutoFileName(), ModsHasher.BuildAutoFileName());
    }

    [Fact]
    public void HeaderHasher_BuildAutoFileName_DiffersAcrossCalls() {
        Assert.NotEqual(HeaderHasher.BuildAutoFileName(), HeaderHasher.BuildAutoFileName());
    }

    // --- CODE-11 (see IMPROVEMENT-PLAN.md): a user-supplied --version-tag must never reach the filename
    // unsanitized, and an auto-generated name must never silently overwrite an existing file. ---

    [Fact]
    public void GameHasher_BuildAutoFileName_SanitizesInvalidFileNameCharactersInVersionTag() {
        var manifest = new GameManifest { VersionTag = "release/1.0" };
        var fileName = GameHasher.BuildAutoFileName(manifest);

        Assert.DoesNotContain("release/1.0", fileName);
        Assert.Contains("release-1.0", fileName);
    }

    [Fact]
    public void GameHasher_BuildAutoFileName_BoundsAnExcessivelyLongVersionTag() {
        var manifest = new GameManifest { VersionTag = new string('x', 200) };
        var fileName = GameHasher.BuildAutoFileName(manifest);

        Assert.True(fileName.Length < 150, $"Expected a bounded filename, got {fileName.Length} characters.");
    }

    [Fact]
    public void AllocateUniquePath_SkipsCandidatesThatAlreadyExist() {
        var dir = Path.Combine(Path.GetTempPath(), $"allocate-unique-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try {
            var taken = Path.Combine(dir, "taken.json");
            File.WriteAllText(taken, "existing");

            var attempts = 0;
            var candidates = new[] { taken, Path.Combine(dir, "free.json") };
            var result = AutoFileNaming.AllocateUniquePath(() => candidates[attempts++]);

            Assert.Equal(candidates[1], result);
            Assert.Equal(2, attempts);
        } finally {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AllocateUniquePath_ThrowsAfterExhaustingAttemptsIfEveryCandidateIsTaken() {
        var path = Path.Combine(Path.GetTempPath(), $"allocate-unique-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "existing");
        try {
            Assert.Throws<IOException>(() => AutoFileNaming.AllocateUniquePath(() => path));
        } finally {
            File.Delete(path);
        }
    }
}
