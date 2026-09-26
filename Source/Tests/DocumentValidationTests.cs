using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-01 (see IMPROVEMENT-PLAN.md): reading the wrong kind of document must
/// fail loudly and name the file/expected kind, instead of Newtonsoft's default lenient deserialization
/// silently leaving the wrong-shaped properties at their empty defaults.</summary>
public class DocumentValidationTests {
    private static string WriteTempJson(string json) {
        var path = Path.Combine(Path.GetTempPath(), $"doc-validation-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ReadFile_RejectsModManifestReadAsGameManifest() {
        var path = WriteTempJson("""{ "generatedAt": "now", "modsFolder": "x", "mods": {} }""");
        try {
            var ex = Assert.Throws<DocumentValidationException>(() => JsonUtil.ReadFile<GameManifest>(path));
            Assert.Contains("game manifest", ex.Message);
            Assert.Contains(path, ex.Message);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_RejectsGameManifestReadAsModManifest() {
        var path = WriteTempJson("""{ "ueVersion": "GAME_UE5_3", "assets": {} }""");
        try {
            var ex = Assert.Throws<DocumentValidationException>(() => JsonUtil.ReadFile<ModManifest>(path));
            Assert.Contains("mods manifest", ex.Message);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_RejectsEmptyObjectReadAsGameComparison() {
        var path = WriteTempJson("{}");
        try {
            var ex = Assert.Throws<DocumentValidationException>(() => JsonUtil.ReadFile<GameCompareResult>(path));
            Assert.Contains("game comparison", ex.Message);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_RejectsWrongJsonTypeForARequiredProperty() {
        // "assets" should be an object, not a string.
        var path = WriteTempJson("""{ "ueVersion": "GAME_UE5_3", "assets": "not-an-object" }""");
        try {
            var ex = Assert.Throws<DocumentValidationException>(() => JsonUtil.ReadFile<GameManifest>(path));
            Assert.Contains("assets", ex.Message);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_RejectsMalformedJson() {
        var path = WriteTempJson("{ this is not valid json");
        try {
            Assert.Throws<DocumentValidationException>(() => JsonUtil.ReadFile<GameManifest>(path));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_AllowsGenuinelyEmptyGameManifest() {
        // A real, valid manifest for a game with zero assets (or one that just started) - assets present but
        // empty must still be accepted, distinguishing "empty" from "wrong document" is the whole point.
        var path = WriteTempJson("""{ "ueVersion": "GAME_UE5_3", "assets": {} }""");
        try {
            var manifest = JsonUtil.ReadFile<GameManifest>(path);
            Assert.Empty(manifest.Assets);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_AllowsLegacyManifestMissingNewerOptionalFields() {
        // Simulates a manifest written before DetectedGameVersion/VersionTag/Errors existed - only the
        // always-required fields are present. Must still load without complaint.
        var path = WriteTempJson("""{ "ueVersion": "GAME_UE5_3", "assets": { "a.uasset": { "hash": "ABC", "size": 10 } } }""");
        try {
            var manifest = JsonUtil.ReadFile<GameManifest>(path);
            Assert.Single(manifest.Assets);
            Assert.Null(manifest.DetectedGameVersion);
            Assert.Empty(manifest.Errors);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_AllowsGenuinelyEmptyGameComparison() {
        var path = WriteTempJson("""{ "generatedAt": "now", "added": [], "removed": [], "changed": [], "unchangedCount": 0 }""");
        try {
            var result = JsonUtil.ReadFile<GameCompareResult>(path);
            Assert.Empty(result.Added);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadFile_AllowsHeaderManifestWithOnlyCxxSection() {
        // A real header manifest can legitimately have only one of CXXHeaderDump/UHTHeaderDump available.
        var path = WriteTempJson("""{ "cxxTypes": { "AActor": {} }, "cxxEnums": {}, "uhtTypes": {} }""");
        try {
            var manifest = JsonUtil.ReadFile<HeaderManifest>(path);
            Assert.Single(manifest.CxxTypes);
            Assert.Empty(manifest.UhtTypes);
        } finally {
            File.Delete(path);
        }
    }
}
