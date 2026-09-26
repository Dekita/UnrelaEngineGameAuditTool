using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-07's output-ownership slice (see IMPROVEMENT-PLAN.md): two GUI tabs
/// started back-to-back must not be able to race to write the same explicit output path. Each test uses a
/// path unique to itself, since <see cref="OperationCoordinator"/> is process-global state shared with every
/// other test here (and, in the real app, every tab).</summary>
public class OperationCoordinatorTests {
    [Fact]
    public void Claim_AllowsAFreshPath() {
        using var claim = OperationCoordinator.Claim("oc-test-fresh.json");
        Assert.NotNull(claim);
    }

    [Fact]
    public void Claim_RejectsAPathAlreadyHeld() {
        using var first = OperationCoordinator.Claim("oc-test-held.json");

        Assert.Throws<InvalidOperationException>(() => OperationCoordinator.Claim("oc-test-held.json"));
    }

    [Fact]
    public void Claim_DetectsCollisionAcrossRelativeAndAbsoluteForms() {
        var absolute = Path.GetFullPath("oc-test-relvsabs.json");
        using var first = OperationCoordinator.Claim(absolute);

        Assert.Throws<InvalidOperationException>(() => OperationCoordinator.Claim("oc-test-relvsabs.json"));
    }

    [Fact]
    public void Claim_DetectsCollisionCaseInsensitively() {
        using var first = OperationCoordinator.Claim("oc-test-CASE.json");

        Assert.Throws<InvalidOperationException>(() => OperationCoordinator.Claim("oc-test-case.json"));
    }

    [Fact]
    public void Dispose_ReleasesThePathForReuse() {
        var claim = OperationCoordinator.Claim("oc-test-reusable.json");
        claim.Dispose();

        var exception = Record.Exception(() => {
            using var second = OperationCoordinator.Claim("oc-test-reusable.json");
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_TwiceIsSafeAndDoesNotReleaseSomeoneElsesLaterClaim() {
        var claim = OperationCoordinator.Claim("oc-test-double-dispose.json");
        claim.Dispose();

        using var other = OperationCoordinator.Claim("oc-test-double-dispose.json");
        claim.Dispose(); // Must not release `other`'s claim on the same path.

        Assert.Throws<InvalidOperationException>(() => OperationCoordinator.Claim("oc-test-double-dispose.json"));
    }

    [Fact]
    public void Claim_IgnoresBlankAndNullPaths() {
        var exception = Record.Exception(() => {
            using var claim = OperationCoordinator.Claim("", null);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Claim_OfMultiplePathsIsAllOrNothing() {
        using var held = OperationCoordinator.Claim("oc-test-atomic-held.json");

        Assert.Throws<InvalidOperationException>(() =>
            OperationCoordinator.Claim("oc-test-atomic-fresh.json", "oc-test-atomic-held.json"));

        // The failed claim must not have partially claimed the non-colliding path.
        var exception = Record.Exception(() => {
            using var second = OperationCoordinator.Claim("oc-test-atomic-fresh.json");
        });
        Assert.Null(exception);
    }
}
