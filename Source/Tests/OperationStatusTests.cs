using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for UI-04's status-wording slice (see IMPROVEMENT-PLAN.md): every view's
/// status label goes through this one place, so a wording change or regression here would silently affect
/// all six action screens at once.</summary>
public class OperationStatusTests {
    [Fact]
    public void Idle_IsBlank() {
        Assert.Equal("", OperationStatusText.For(OperationStatus.Idle));
    }

    [Fact]
    public void Running_IsDistinctFromIdle() {
        Assert.NotEqual("", OperationStatusText.For(OperationStatus.Running));
    }

    [Fact]
    public void Completed_WithNoDetail_DoesNotMentionADetail() {
        var text = OperationStatusText.For(OperationStatus.Completed);
        Assert.Contains("Completed", text);
        Assert.DoesNotContain(" - ", text);
    }

    [Fact]
    public void Completed_WithDetail_IncludesIt() {
        var text = OperationStatusText.For(OperationStatus.Completed, "3 asset(s) failed to hash");
        Assert.Contains("Completed", text);
        Assert.Contains("3 asset(s) failed to hash", text);
    }

    [Fact]
    public void Partial_AlwaysMentionsTheDetailAndPointsAtTheLog() {
        var text = OperationStatusText.For(OperationStatus.Partial, "2 uncertain");
        Assert.Contains("2 uncertain", text);
        Assert.Contains("log", text);
    }

    [Fact]
    public void Partial_IsTextuallyDistinctFromCompleted() {
        var completed = OperationStatusText.For(OperationStatus.Completed);
        var partial = OperationStatusText.For(OperationStatus.Partial, "1 error");

        Assert.NotEqual(completed, partial);
    }

    [Fact]
    public void FailedToSave_IsDistinctFromPlainFailed() {
        var failed = OperationStatusText.For(OperationStatus.Failed);
        var failedToSave = OperationStatusText.For(OperationStatus.FailedToSave);

        Assert.NotEqual(failed, failedToSave);
    }

    [Fact]
    public void Cancelled_MentionsCancellation() {
        Assert.Contains("Cancel", OperationStatusText.For(OperationStatus.Cancelled));
    }
}
