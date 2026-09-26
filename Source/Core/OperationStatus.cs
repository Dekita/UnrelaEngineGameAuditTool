namespace DekUnrealGameAudit.Core;

/// <summary>Where a Run currently stands, shown in a small always-visible status label next to the Run
/// button on every action screen - separate from the scrolling log, which can be capped/scrolled past and
/// isn't a reliable place to answer "is this still running, and did it actually finish cleanly?" at a
/// glance. Deliberately coarse (no Mounting/Parsing/Comparing sub-stages) - the underlying hashers/compare
/// engines don't report stage boundaries today, and adding that instrumentation is separate, larger work.</summary>
public enum OperationStatus {
    Idle,
    Running,
    Saving,
    Completed,
    /// <summary>Completed, but with skipped/errored assets or uncertain comparison results - must never
    /// look identical to a clean Completed, since a mod report built on a Partial result isn't a confident
    /// "safe" answer.</summary>
    Partial,
    Cancelled,
    Failed,
    /// <summary>The computation itself succeeded, but writing the result failed (disk full, path now
    /// invalid, file locked, etc). Kept distinct from a plain Failed so the UI can offer retrying just the
    /// save without redoing potentially expensive work that already completed.</summary>
    FailedToSave,
}

public static class OperationStatusText {
    public static string For(OperationStatus status, string? detail = null) => status switch {
        OperationStatus.Idle => "",
        OperationStatus.Running => "Running...",
        OperationStatus.Saving => "Saving...",
        OperationStatus.Completed => "Completed" + (detail != null ? $" - {detail}" : ""),
        OperationStatus.Partial => $"Completed - {detail}, see log",
        OperationStatus.Cancelled => "Cancelled" + (detail != null ? $" - {detail}" : ""),
        OperationStatus.Failed => "Failed - see log",
        OperationStatus.FailedToSave => "Computed, but failed to save - see log",
        _ => "",
    };
}
