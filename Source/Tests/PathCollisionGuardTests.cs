using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-02 (see IMPROVEMENT-PLAN.md): every compare command/view must refuse
/// to write an output over one of its own inputs, or over another output from the same run.</summary>
public class PathCollisionGuardTests {
    [Fact]
    public void CheckNoCollisions_AllowsDistinctPaths() {
        var exception = Record.Exception(() => PathCollisionGuard.CheckNoCollisions(
            inputs: [("old", "old.json"), ("new", "new.json")],
            outputs: [("out", "comparison.json"), ("md", "report.md")]));

        Assert.Null(exception);
    }

    [Fact]
    public void CheckNoCollisions_RejectsOutputEqualToInput_ExactSamePath() {
        Assert.Throws<InvalidOperationException>(() => PathCollisionGuard.CheckNoCollisions(
            inputs: [("old", "manifest.json"), ("new", "new.json")],
            outputs: [("out", "manifest.json")]));
    }

    [Fact]
    public void CheckNoCollisions_RejectsOutputEqualToInput_RelativeVsAbsolute() {
        var absoluteOld = Path.GetFullPath("manifest.json");

        Assert.Throws<InvalidOperationException>(() => PathCollisionGuard.CheckNoCollisions(
            inputs: [("old", absoluteOld), ("new", "new.json")],
            outputs: [("out", "manifest.json")]));
    }

    [Fact]
    public void CheckNoCollisions_RejectsOutputEqualToInput_WindowsCaseVariant() {
        Assert.Throws<InvalidOperationException>(() => PathCollisionGuard.CheckNoCollisions(
            inputs: [("old", "Manifest.JSON"), ("new", "new.json")],
            outputs: [("out", "manifest.json")]));
    }

    [Fact]
    public void CheckNoCollisions_RejectsTwoOutputsWritingTheSameFile() {
        Assert.Throws<InvalidOperationException>(() => PathCollisionGuard.CheckNoCollisions(
            inputs: [("old", "old.json"), ("new", "new.json")],
            outputs: [("json", "report.out"), ("markdown", "report.out")]));
    }

    [Fact]
    public void CheckNoCollisions_IgnoresBlankOrNullOutputs() {
        var exception = Record.Exception(() => PathCollisionGuard.CheckNoCollisions(
            inputs: [("old", "old.json"), ("new", "new.json")],
            outputs: [("out", ""), ("md", null)]));

        Assert.Null(exception);
    }
}
