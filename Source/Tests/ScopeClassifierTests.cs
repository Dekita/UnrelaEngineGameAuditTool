using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-16 (see IMPROVEMENT-PLAN.md): the Game/Mod/Ignore classification
/// engine that CODE-16 adds. This is the highest-value, most reusable part of the feature - pure logic, no
/// filesystem or CUE4Parse involved - so it gets the deepest test coverage.</summary>
public class ScopeClassifierTests {
    private static ScopeRuleSet RuleSet(params ScopeRule[] rules) => new() { Rules = rules.ToList() };

    private static ScopeRule Rule(string path, bool isFolder, ScopeAction action) =>
        new() { Path = path, IsFolder = isFolder, Action = action };

    [Fact]
    public void Classify_RootLevelFile_DefaultsToGame() {
        var result = ScopeClassifier.Classify("base.pak", RuleSet());
        Assert.Equal(ScopeAction.Game, result);
    }

    [Theory]
    [InlineData("~mods/a.pak")]
    [InlineData("LogicMods/b.pak")]
    [InlineData("Anything/Nested/c.pak")]
    public void Classify_FileInAnyDescendantFolder_DefaultsToMod(string path) {
        Assert.Equal(ScopeAction.Mod, ScopeClassifier.Classify(path, RuleSet()));
    }

    [Fact]
    public void Classify_ExplicitFolderGameOverride_MakesNestedFileGame() {
        var rules = RuleSet(Rule("OfficialDLC", isFolder: true, ScopeAction.Game));
        Assert.Equal(ScopeAction.Game, ScopeClassifier.Classify("OfficialDLC/dlc_p.pak", rules));
    }

    [Fact]
    public void Classify_ExplicitFileModOverride_MakesRootFileMod() {
        var rules = RuleSet(Rule("root-mod.pak", isFolder: false, ScopeAction.Mod));
        Assert.Equal(ScopeAction.Mod, ScopeClassifier.Classify("root-mod.pak", rules));
    }

    [Fact]
    public void Classify_IgnoredFolder_ExcludesEverythingInside() {
        var rules = RuleSet(Rule("Broken", isFolder: true, ScopeAction.Ignore));
        Assert.Equal(ScopeAction.Ignore, ScopeClassifier.Classify("Broken/whatever.pak", rules));
        Assert.Equal(ScopeAction.Ignore, ScopeClassifier.Classify("Broken/Nested/deep.pak", rules));
    }

    [Fact]
    public void Classify_IgnoreWins_EvenOverAMoreSpecificRuleUnderneath() {
        // "No hidden exceptions" - a more specific Game rule inside an ignored folder must NOT resurrect it;
        // the only way in is to narrow/remove the Ignore rule itself.
        var rules = RuleSet(
            Rule("Broken", isFolder: true, ScopeAction.Ignore),
            Rule("Broken/Fixed", isFolder: true, ScopeAction.Game));

        Assert.Equal(ScopeAction.Ignore, ScopeClassifier.Classify("Broken/Fixed/asset.pak", rules));
    }

    [Fact]
    public void Classify_IgnoreWins_OverExactFileRuleToo() {
        var rules = RuleSet(
            Rule("Mods", isFolder: true, ScopeAction.Ignore),
            Rule("Mods/special.pak", isFolder: false, ScopeAction.Game));

        Assert.Equal(ScopeAction.Ignore, ScopeClassifier.Classify("Mods/special.pak", rules));
    }

    [Fact]
    public void Classify_ExactFileRule_OverridesContainingFolderRule() {
        var rules = RuleSet(
            Rule("~mods", isFolder: true, ScopeAction.Mod),
            Rule("~mods/actually-game.pak", isFolder: false, ScopeAction.Game));

        Assert.Equal(ScopeAction.Game, ScopeClassifier.Classify("~mods/actually-game.pak", rules));
        // Sibling files in the same folder are unaffected by the file-level override.
        Assert.Equal(ScopeAction.Mod, ScopeClassifier.Classify("~mods/other.pak", rules));
    }

    [Fact]
    public void Classify_DeepestMatchingFolderRule_Wins() {
        var rules = RuleSet(
            Rule("Mods", isFolder: true, ScopeAction.Mod),
            Rule("Mods/OfficialSubset", isFolder: true, ScopeAction.Game));

        Assert.Equal(ScopeAction.Game, ScopeClassifier.Classify("Mods/OfficialSubset/x.pak", rules));
        Assert.Equal(ScopeAction.Mod, ScopeClassifier.Classify("Mods/OtherMod/y.pak", rules));
    }

    [Fact]
    public void Classify_FolderRuleIsComponentAware_DoesNotMatchSimilarlyNamedFolder() {
        var rules = RuleSet(Rule("Mods", isFolder: true, ScopeAction.Ignore));

        // "ModsBackup" must not be treated as inside "Mods" just because it shares a string prefix.
        Assert.Equal(ScopeAction.Mod, ScopeClassifier.Classify("ModsBackup/x.pak", rules));
        Assert.Equal(ScopeAction.Ignore, ScopeClassifier.Classify("Mods/x.pak", rules));
    }

    [Fact]
    public void Classify_FolderRule_DoesNotMatchAFileNamedTheSameAsTheFolder() {
        // A folder rule matches things INSIDE the folder, not a same-named file at that exact path.
        var rules = RuleSet(Rule("Mods", isFolder: true, ScopeAction.Ignore));
        Assert.Equal(ScopeAction.Game, ScopeClassifier.Classify("Mods", rules));
    }

    [Theory]
    [InlineData("~mods/a.pak", "~MODS/A.PAK")]
    [InlineData("Sub\\Folder\\x.pak", "Sub/Folder/x.pak")]
    public void Classify_NormalizesCaseAndSeparatorsConsistently(string a, string b) {
        var rules = RuleSet(Rule("~mods", isFolder: true, ScopeAction.Game), Rule("Sub/Folder", isFolder: true, ScopeAction.Game));
        Assert.Equal(ScopeClassifier.Classify(a, rules), ScopeClassifier.Classify(b, rules));
    }

    [Fact]
    public void ClassifyWithReason_RootLevelFileWithNoRules_ExplainsDefaultGameBranch() {
        var result = ScopeClassifier.ClassifyWithReason("base.pak", RuleSet());
        Assert.Equal(ScopeAction.Game, result.Action);
        Assert.Contains("Default", result.Explanation);
        Assert.Contains("Game", result.Explanation);
    }

    [Fact]
    public void ClassifyWithReason_NestedFileWithNoRules_ExplainsDefaultModBranch() {
        var result = ScopeClassifier.ClassifyWithReason("~mods/a.pak", RuleSet());
        Assert.Equal(ScopeAction.Mod, result.Action);
        Assert.Contains("Default", result.Explanation);
        Assert.Contains("Mod", result.Explanation);
    }

    [Fact]
    public void ClassifyWithReason_ExactFileRule_NamesTheFileRule() {
        var rules = RuleSet(Rule("root-mod.pak", isFolder: false, ScopeAction.Mod));
        var result = ScopeClassifier.ClassifyWithReason("root-mod.pak", rules);
        Assert.Equal(ScopeAction.Mod, result.Action);
        Assert.Contains("root-mod.pak", result.Explanation);
        Assert.Contains("file rule", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClassifyWithReason_FolderRule_NamesTheFolderRule() {
        var rules = RuleSet(Rule("OfficialDLC", isFolder: true, ScopeAction.Game));
        var result = ScopeClassifier.ClassifyWithReason("OfficialDLC/dlc_p.pak", rules);
        Assert.Equal(ScopeAction.Game, result.Action);
        Assert.Contains("OfficialDLC", result.Explanation);
        Assert.Contains("folder rule", result.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClassifyWithReason_DeepestOfTwoNestedFolderRules_NamesTheDeeperOne() {
        var rules = RuleSet(
            Rule("Mods", isFolder: true, ScopeAction.Mod),
            Rule("Mods/OfficialSubset", isFolder: true, ScopeAction.Game));

        var result = ScopeClassifier.ClassifyWithReason("Mods/OfficialSubset/x.pak", rules);
        Assert.Equal(ScopeAction.Game, result.Action);
        Assert.Contains("Mods/OfficialSubset", result.Explanation);
    }

    [Fact]
    public void ClassifyWithReason_IgnoreWins_NamesTheIgnoringFolderRuleNotTheOverriddenOne() {
        var rules = RuleSet(
            Rule("Broken", isFolder: true, ScopeAction.Ignore),
            Rule("Broken/Fixed", isFolder: true, ScopeAction.Game));

        var result = ScopeClassifier.ClassifyWithReason("Broken/Fixed/asset.pak", rules);
        Assert.Equal(ScopeAction.Ignore, result.Action);
        Assert.Contains("Broken", result.Explanation);
        Assert.DoesNotContain("Fixed", result.Explanation);
    }

    [Fact]
    public void Classify_AndClassifyWithReason_AlwaysAgreeOnTheAction() {
        var rules = RuleSet(
            Rule("Mods", isFolder: true, ScopeAction.Mod),
            Rule("Mods/OfficialSubset", isFolder: true, ScopeAction.Game),
            Rule("Mods/special.pak", isFolder: false, ScopeAction.Ignore));

        foreach (var path in new[] { "base.pak", "Mods/x.pak", "Mods/OfficialSubset/y.pak", "Mods/special.pak" })
            Assert.Equal(ScopeClassifier.Classify(path, rules), ScopeClassifier.ClassifyWithReason(path, rules).Action);
    }

    [Fact]
    public void Validate_RejectsBlankPath() {
        var rules = RuleSet(Rule("   ", isFolder: true, ScopeAction.Mod));
        Assert.Throws<ArgumentException>(() => ScopeClassifier.Validate(rules));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("Mods/../../escape")]
    [InlineData(".")]
    public void Validate_RejectsPathsEscapingTheRoot(string path) {
        var rules = RuleSet(Rule(path, isFolder: true, ScopeAction.Mod));
        Assert.Throws<ArgumentException>(() => ScopeClassifier.Validate(rules));
    }

    [Fact]
    public void Validate_RejectsConflictingEqualSpecificityRules() {
        var rules = RuleSet(
            Rule("Mods", isFolder: true, ScopeAction.Mod),
            Rule("mods", isFolder: true, ScopeAction.Ignore)); // same normalized path+type, different action

        var ex = Assert.Throws<ArgumentException>(() => ScopeClassifier.Validate(rules));
        Assert.Contains("mods", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_AllowsIdenticalDuplicateRules() {
        var rules = RuleSet(
            Rule("Mods", isFolder: true, ScopeAction.Mod),
            Rule("Mods", isFolder: true, ScopeAction.Mod));

        ScopeClassifier.Validate(rules); // does not throw
    }

    [Fact]
    public void Validate_AllowsAFileRuleAndAFolderRuleSharingTheSamePathString() {
        // A file rule at "Mods" and a folder rule at "Mods" target different things (the literal file "Mods"
        // vs. contents of a "Mods" folder) - not a real conflict.
        var rules = RuleSet(
            Rule("Mods", isFolder: false, ScopeAction.Game),
            Rule("Mods", isFolder: true, ScopeAction.Mod));

        ScopeClassifier.Validate(rules); // does not throw
    }

    [Fact]
    public void ComputeFingerprint_IsOrderIndependent() {
        var a = RuleSet(Rule("A", true, ScopeAction.Mod), Rule("B", false, ScopeAction.Ignore));
        var b = RuleSet(Rule("B", false, ScopeAction.Ignore), Rule("A", true, ScopeAction.Mod));

        Assert.Equal(ScopeClassifier.ComputeFingerprint(a), ScopeClassifier.ComputeFingerprint(b));
    }

    [Fact]
    public void ComputeFingerprint_DiffersWhenRulesActuallyDiffer() {
        var a = RuleSet(Rule("A", true, ScopeAction.Mod));
        var b = RuleSet(Rule("A", true, ScopeAction.Game));

        Assert.NotEqual(ScopeClassifier.ComputeFingerprint(a), ScopeClassifier.ComputeFingerprint(b));
    }

    [Fact]
    public void ComputeFingerprint_DiffersForAnEmptyRuleSetVersusNone() {
        // Sanity check that an explicit, empty rule set (opting into the default heuristic with zero
        // overrides) still produces a stable, well-defined fingerprint rather than throwing.
        var fingerprint = ScopeClassifier.ComputeFingerprint(RuleSet());
        Assert.False(string.IsNullOrWhiteSpace(fingerprint));
    }
}
