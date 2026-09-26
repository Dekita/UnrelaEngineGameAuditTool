using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

public class GameCompareEngineTests {
    private static GameManifest Manifest(string ueVersion, params (string Path, string Hash)[] assets) {
        var manifest = new GameManifest { UeVersion = ueVersion };
        foreach (var (path, hash) in assets)
            manifest.Assets[path] = new AssetEntry { Hash = hash, Size = 1 };
        return manifest;
    }

    private static void WithError(GameManifest manifest, string path, string message = "read failed") =>
        manifest.Errors.Add(new AssetError { Path = path, Message = message });

    [Fact]
    public void Compare_DetectsAddedRemovedChangedAndUnchanged() {
        var oldManifest = Manifest("GAME_UE5_3",
            ("Content/Kept.uasset", "AAA"),
            ("Content/Changed.uasset", "BBB"),
            ("Content/Removed.uasset", "CCC"));
        var newManifest = Manifest("GAME_UE5_3",
            ("Content/Kept.uasset", "AAA"),
            ("Content/Changed.uasset", "BBB2"),
            ("Content/Added.uasset", "DDD"));

        var result = GameCompareEngine.Compare(oldManifest, newManifest);

        Assert.Equal(new[] { "Content/Added.uasset" }, result.Added);
        Assert.Equal(new[] { "Content/Removed.uasset" }, result.Removed);
        Assert.Equal("Content/Changed.uasset", Assert.Single(result.Changed).Path);
        Assert.Equal(1, result.UnchangedCount);
    }

    [Fact]
    public void Compare_HashComparisonIsCaseInsensitive() {
        var oldManifest = Manifest("GAME_UE5_3", ("Content/A.uasset", "abc123"));
        var newManifest = Manifest("GAME_UE5_3", ("Content/A.uasset", "ABC123"));

        var result = GameCompareEngine.Compare(oldManifest, newManifest);

        Assert.Empty(result.Changed);
        Assert.Equal(1, result.UnchangedCount);
    }

    [Fact]
    public void Compare_WarnsWhenUeVersionsDiffer() {
        var oldManifest = Manifest("GAME_UE5_3");
        var newManifest = Manifest("GAME_UE5_4");

        var warnings = new List<string>();
        GameCompareEngine.Compare(oldManifest, newManifest, onLog: warnings.Add);

        Assert.Contains(warnings, w => w.Contains("GAME_UE5_3") && w.Contains("GAME_UE5_4"));
    }

    [Fact]
    public void Compare_DoesNotWarnWhenUeVersionsMatch() {
        var oldManifest = Manifest("GAME_UE5_3");
        var newManifest = Manifest("GAME_UE5_3");

        var warnings = new List<string>();
        GameCompareEngine.Compare(oldManifest, newManifest, onLog: warnings.Add);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Compare_WarnsWhenHashSchemeVersionsDiffer() {
        var oldManifest = Manifest("GAME_UE5_3");
        var newManifest = Manifest("GAME_UE5_3");
        oldManifest.HashSchemeVersion = 1;
        newManifest.HashSchemeVersion = 2;

        var warnings = new List<string>();
        GameCompareEngine.Compare(oldManifest, newManifest, onLog: warnings.Add);

        Assert.Contains(warnings, w => w.Contains("hash scheme", StringComparison.OrdinalIgnoreCase) && w.Contains("v1") && w.Contains("v2"));
    }

    [Fact]
    public void Compare_DoesNotWarnWhenHashSchemeVersionsMatch() {
        var oldManifest = Manifest("GAME_UE5_3");
        var newManifest = Manifest("GAME_UE5_3");
        oldManifest.HashSchemeVersion = 2;
        newManifest.HashSchemeVersion = 2;

        var warnings = new List<string>();
        GameCompareEngine.Compare(oldManifest, newManifest, onLog: warnings.Add);

        Assert.Empty(warnings);
    }

    [Fact]
    public void RenderMarkdown_IncludesAddedRemovedAndChangedSections() {
        var oldManifest = Manifest("GAME_UE5_3",
            ("Content/Kept.uasset", "AAA"),
            ("Content/Changed.uasset", "BBB"),
            ("Content/Removed.uasset", "CCC"));
        var newManifest = Manifest("GAME_UE5_3",
            ("Content/Kept.uasset", "AAA"),
            ("Content/Changed.uasset", "BBB2"),
            ("Content/Added.uasset", "DDD"));

        var result = GameCompareEngine.Compare(oldManifest, newManifest);
        var markdown = GameCompareEngine.RenderMarkdown(result);

        Assert.Contains("Content/Added.uasset", markdown);
        Assert.Contains("Content/Removed.uasset", markdown);
        Assert.Contains("Content/Changed.uasset", markdown);
        Assert.Contains("BBB", markdown);
        Assert.Contains("BBB2", markdown);
    }

    // CODE-04: a scan error must never be misreported as a confident Added/Removed classification.
    [Fact]
    public void Compare_APathThatFailedInTheOldScanIsUncertainNotAdded() {
        var oldManifest = Manifest("GAME_UE5_3");
        WithError(oldManifest, "Content/Flaky.uasset", "corrupt header");
        var newManifest = Manifest("GAME_UE5_3", ("Content/Flaky.uasset", "AAA"));

        var result = GameCompareEngine.Compare(oldManifest, newManifest);

        Assert.Empty(result.Added);
        var uncertain = Assert.Single(result.Uncertain);
        Assert.Equal("Content/Flaky.uasset", uncertain.Path);
        Assert.Contains("corrupt header", uncertain.Reason);
    }

    [Fact]
    public void Compare_APathThatFailsInTheNewScanIsUncertainNotRemoved() {
        var oldManifest = Manifest("GAME_UE5_3", ("Content/Flaky.uasset", "AAA"));
        var newManifest = Manifest("GAME_UE5_3");
        WithError(newManifest, "Content/Flaky.uasset", "provider timeout");

        var result = GameCompareEngine.Compare(oldManifest, newManifest);

        Assert.Empty(result.Removed);
        var uncertain = Assert.Single(result.Uncertain);
        Assert.Equal("Content/Flaky.uasset", uncertain.Path);
        Assert.Contains("provider timeout", uncertain.Reason);
    }

    [Fact]
    public void Compare_APathThatFailsInBothScansIsSurfacedAsUncertain() {
        var oldManifest = Manifest("GAME_UE5_3");
        WithError(oldManifest, "Content/AlwaysFlaky.uasset", "old failure");
        var newManifest = Manifest("GAME_UE5_3");
        WithError(newManifest, "Content/AlwaysFlaky.uasset", "new failure");

        var result = GameCompareEngine.Compare(oldManifest, newManifest);

        var uncertain = Assert.Single(result.Uncertain);
        Assert.Equal("Content/AlwaysFlaky.uasset", uncertain.Path);
        Assert.Contains("old failure", uncertain.Reason);
        Assert.Contains("new failure", uncertain.Reason);
    }

    [Fact]
    public void Compare_AnUnrelatedErrorDoesNotAffectOtherPathsClassification() {
        var oldManifest = Manifest("GAME_UE5_3", ("Content/Fine.uasset", "AAA"));
        WithError(oldManifest, "Content/Unrelated.uasset");
        var newManifest = Manifest("GAME_UE5_3", ("Content/Fine.uasset", "AAA"), ("Content/GenuinelyNew.uasset", "BBB"));

        var result = GameCompareEngine.Compare(oldManifest, newManifest);

        Assert.Equal(new[] { "Content/GenuinelyNew.uasset" }, result.Added);
        Assert.Equal(1, result.UnchangedCount);
        Assert.Empty(result.Uncertain);
    }

    [Fact]
    public void Compare_WarnsWhenThereAreUncertainAssets() {
        var oldManifest = Manifest("GAME_UE5_3");
        WithError(oldManifest, "Content/Flaky.uasset");
        var newManifest = Manifest("GAME_UE5_3", ("Content/Flaky.uasset", "AAA"));

        var warnings = new List<string>();
        GameCompareEngine.Compare(oldManifest, newManifest, onLog: warnings.Add);

        Assert.Contains(warnings, w => w.Contains("1 asset") && w.Contains("Uncertain"));
    }

    [Fact]
    public void RenderMarkdown_IncludesTheUncertainSectionWhenPresent() {
        var oldManifest = Manifest("GAME_UE5_3");
        WithError(oldManifest, "Content/Flaky.uasset", "corrupt header");
        var newManifest = Manifest("GAME_UE5_3", ("Content/Flaky.uasset", "AAA"));

        var result = GameCompareEngine.Compare(oldManifest, newManifest);
        var markdown = GameCompareEngine.RenderMarkdown(result);

        Assert.Contains("Uncertain", markdown);
        Assert.Contains("Content/Flaky.uasset", markdown);
        Assert.Contains("corrupt header", markdown);
    }

    [Fact]
    public void RenderMarkdown_OmitsTheUncertainSectionWhenThereIsNone() {
        var oldManifest = Manifest("GAME_UE5_3", ("Content/A.uasset", "AAA"));
        var newManifest = Manifest("GAME_UE5_3", ("Content/A.uasset", "AAA"));

        var result = GameCompareEngine.Compare(oldManifest, newManifest);
        var markdown = GameCompareEngine.RenderMarkdown(result);

        Assert.DoesNotContain("## ⚠ Uncertain", markdown);
    }

    // CODE-10: an asset path containing a pipe must not corrupt the Markdown table it's rendered into.
    [Fact]
    public void RenderMarkdown_PathContainingPipe_DoesNotBreakTheTable() {
        var oldManifest = Manifest("GAME_UE5_3");
        var newManifest = Manifest("GAME_UE5_3", ("Content/Weird|Path.uasset", "AAA"));

        var result = GameCompareEngine.Compare(oldManifest, newManifest);
        var markdown = GameCompareEngine.RenderMarkdown(result);

        Assert.Contains("Content/Weird\\|Path.uasset", markdown);
    }
}
