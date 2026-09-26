using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

public class ModsCompareEngineTests {
    private static GameCompareResult Diff(string[]? added = null, string[]? removed = null, string[]? changed = null, string[]? uncertain = null) {
        var result = new GameCompareResult();
        result.Added.AddRange(added ?? []);
        result.Removed.AddRange(removed ?? []);
        foreach (var path in changed ?? [])
            result.Changed.Add(new ChangedAsset { Path = path, OldHash = "A", NewHash = "B" });
        foreach (var path in uncertain ?? [])
            result.Uncertain.Add(new UncertainAsset { Path = path, Reason = "test" });
        return result;
    }

    private static ModManifest Mods(params (string Name, string[] Assets)[] mods) {
        var manifest = new ModManifest();
        foreach (var (name, assets) in mods)
            manifest.Mods[name] = new ModInfo { Assets = assets.ToList() };
        return manifest;
    }

    [Fact]
    public void Compare_FlagsModsTouchingChangedOrRemovedAssets() {
        var diff = Diff(changed: ["Content/Weapon.uasset"], removed: ["Content/OldItem.uasset"]);
        var mods = Mods(
            ("AffectedByChange", ["Content/Weapon.uasset"]),
            ("AffectedByRemoval", ["Content/OldItem.uasset"]),
            ("Untouched", ["Content/Unrelated.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);

        Assert.True(report.Mods["AffectedByChange"].NeedsUpdate);
        Assert.True(report.Mods["AffectedByRemoval"].NeedsUpdate);
        Assert.False(report.Mods["Untouched"].NeedsUpdate);
    }

    // INV-03: an Added asset doesn't make AffectedAssets/NeedsUpdate true (the mod's own override target never
    // changed/vanished - that semantic is unchanged), but it's not silently invisible either: the mod now
    // collides with new official content it never touched before, which is its own distinct signal.
    [Fact]
    public void Compare_AddedAssetMatchingAModPath_IsNotAffectedButIsFlaggedAsANewCollision() {
        var diff = Diff(added: ["Content/BrandNew.uasset"]);
        var mods = Mods(("SomeMod", ["Content/BrandNew.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var entry = report.Mods["SomeMod"];

        Assert.False(entry.NeedsUpdate);
        Assert.Empty(entry.AffectedAssets);
        Assert.True(entry.HasNewCollision);
        Assert.Equal(["Content/BrandNew.uasset"], entry.CollidingAssets);
        Assert.True(entry.NeedsReview);
    }

    [Fact]
    public void Compare_ModWithBothAStaleOverrideAndANewCollision_ReportsBoth() {
        var diff = Diff(changed: ["Content/Weapon.uasset"], added: ["Content/BrandNew.uasset"]);
        var mods = Mods(("SomeMod", ["Content/Weapon.uasset", "Content/BrandNew.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var entry = report.Mods["SomeMod"];

        Assert.True(entry.NeedsUpdate);
        Assert.Equal(["Content/Weapon.uasset"], entry.AffectedAssets);
        Assert.True(entry.HasNewCollision);
        Assert.Equal(["Content/BrandNew.uasset"], entry.CollidingAssets);
    }

    [Fact]
    public void Compare_NoCollision_HasNewCollisionIsFalseAndCollidingAssetsIsEmpty() {
        var diff = Diff(changed: ["Content/Weapon.uasset"]);
        var mods = Mods(("SomeMod", ["Content/Weapon.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var entry = report.Mods["SomeMod"];

        Assert.False(entry.HasNewCollision);
        Assert.Empty(entry.CollidingAssets);
    }

    [Fact]
    public void Summarize_SplitsNeedsUpdateFromUpToDateAndSortsByName() {
        var diff = Diff(changed: ["Content/A.uasset"]);
        var mods = Mods(
            ("Zeta", ["Content/A.uasset"]),
            ("Alpha", ["Content/A.uasset"]),
            ("Fine", ["Content/Other.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var (needsUpdate, needsReview, upToDate) = ModsCompareEngine.Summarize(report);

        Assert.Equal(["Alpha", "Zeta"], needsUpdate.Select(m => m.Key));
        Assert.Empty(needsReview);
        Assert.Equal(1, upToDate);
    }

    // CODE-04: a mod referencing an asset the game scan couldn't confirm (because one or both sides had a
    // read error for it) must not be reported as confidently unaffected just because it's absent from
    // Changed/Removed.
    [Fact]
    public void Compare_FlagsModsTouchingUncertainAssetsForReview_NotAsConfirmedNeedingUpdate() {
        var diff = Diff(uncertain: ["Content/Maybe.uasset"]);
        var mods = Mods(("MaybeAffected", ["Content/Maybe.uasset"]), ("Untouched", ["Content/Other.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);

        Assert.False(report.Mods["MaybeAffected"].NeedsUpdate);
        Assert.True(report.Mods["MaybeAffected"].NeedsReview);
        Assert.Equal(["Content/Maybe.uasset"], report.Mods["MaybeAffected"].UncertainAssets);
        Assert.False(report.Mods["Untouched"].NeedsReview);
    }

    [Fact]
    public void Compare_AModConfirmedChangedAndAlsoUncertainKeepsBothSignalsSeparate() {
        var diff = Diff(changed: ["Content/A.uasset"], uncertain: ["Content/B.uasset"]);
        var mods = Mods(("BothMod", ["Content/A.uasset", "Content/B.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var entry = report.Mods["BothMod"];

        Assert.True(entry.NeedsUpdate);
        Assert.Equal(["Content/A.uasset"], entry.AffectedAssets);
        Assert.Equal(["Content/B.uasset"], entry.UncertainAssets);
    }

    [Fact]
    public void Summarize_PutsUncertainOnlyModsInNeedsReviewNotUpToDate() {
        var diff = Diff(changed: ["Content/A.uasset"], uncertain: ["Content/B.uasset"]);
        var mods = Mods(
            ("Changed", ["Content/A.uasset"]),
            ("Uncertain", ["Content/B.uasset"]),
            ("BothSignals", ["Content/A.uasset", "Content/B.uasset"]),
            ("Clean", ["Content/Other.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var (needsUpdate, needsReview, upToDate) = ModsCompareEngine.Summarize(report);

        Assert.Equal(["BothSignals", "Changed"], needsUpdate.Select(m => m.Key));
        Assert.Equal(["Uncertain"], needsReview.Select(m => m.Key));
        Assert.Equal(1, upToDate);
    }

    // CODE-10: a mod name or asset path containing a pipe must not corrupt the Markdown table.
    [Fact]
    public void RenderMarkdown_ModNameContainingPipe_DoesNotBreakTheTable() {
        var diff = Diff(changed: ["Content/A.uasset"]);
        var mods = Mods(("Weird|Mod", ["Content/A.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var markdown = ModsCompareEngine.RenderMarkdown(report);

        Assert.Contains("Weird\\|Mod", markdown);
    }

    // UI-07: a mod referencing a changed/removed asset is a strong signal, not proof of breakage - wording
    // must say "review required," not assert the mod definitely needs updating.
    [Fact]
    public void RenderMarkdown_AffectedMod_IsWordedAsReviewRequiredNotACertainty() {
        var diff = Diff(changed: ["Content/A.uasset"]);
        var mods = Mods(("SomeMod", ["Content/A.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var markdown = ModsCompareEngine.RenderMarkdown(report);

        Assert.Contains("review required", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("need updating", markdown, StringComparison.OrdinalIgnoreCase);
    }

    // INV-03: a collision-only mod (no stale override, no scan uncertainty) must still be visible in the
    // rendered report, with the actual colliding path shown - not silently folded into "unaffected."
    [Fact]
    public void RenderMarkdown_CollisionOnlyMod_AppearsInNeedsReviewWithThePath() {
        var diff = Diff(added: ["Content/BrandNew.uasset"]);
        var mods = Mods(("SomeMod", ["Content/BrandNew.uasset"]));

        var report = ModsCompareEngine.Compare(diff, mods);
        var markdown = ModsCompareEngine.RenderMarkdown(report);

        Assert.Contains("Need review", markdown);
        Assert.Contains("SomeMod", markdown);
        Assert.Contains("Content/BrandNew.uasset", markdown);
    }

    // CODE-18: the mod INVENTORY delta (which containers appeared/disappeared) is a different question from
    // Compare's mod IMPACT assessment - a removed mod must show up here, not be silently absent.
    [Fact]
    public void CompareInventory_DetectsAddedAndRemovedContainers() {
        var before = Mods(("~mods/Keep/keep_p.pak", []), ("~mods/Gone/gone_p.pak", []));
        var after = Mods(("~mods/Keep/keep_p.pak", []), ("~mods/New/new_p.pak", []));

        var (added, removed) = ModsCompareEngine.CompareInventory(before, after);

        Assert.Equal(["~mods/New/new_p.pak"], added);
        Assert.Equal(["~mods/Gone/gone_p.pak"], removed);
    }

    [Fact]
    public void CompareInventory_IdenticalInventories_ReportsNoChanges() {
        var before = Mods(("~mods/A/a_p.pak", []));
        var after = Mods(("~mods/A/a_p.pak", []));

        var (added, removed) = ModsCompareEngine.CompareInventory(before, after);

        Assert.Empty(added);
        Assert.Empty(removed);
    }

    [Fact]
    public void CompareInventory_IsCaseInsensitiveOnContainerKeys() {
        var before = Mods(("~Mods/A/a_p.pak", []));
        var after = Mods(("~mods/a/a_p.pak", []));

        var (added, removed) = ModsCompareEngine.CompareInventory(before, after);

        Assert.Empty(added);
        Assert.Empty(removed);
    }
}
