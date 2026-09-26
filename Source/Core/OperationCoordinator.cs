using System.IO;

namespace DekUnrealGameAudit.Core;

/// <summary>Tracks output paths currently being written by an in-progress run, across all six GUI tabs (the
/// CLI has no concurrent-operation concept to guard, but this has no GUI dependency either, so it lives
/// alongside its sibling <see cref="PathCollisionGuard"/>). Each view's own busy flag (see
/// HashGameFilesView.IsBusy) only stops that one tab from starting a second run on top of itself, and
/// PathCollisionGuard only checks a single run's own inputs/outputs against each other - neither stops two
/// *different* tabs from being started back-to-back and racing to write the same explicit output path. This
/// is the cross-tab counterpart: claim an output path before writing to it, release the claim when the run
/// ends (success, failure or cancellation), and reject a claim that's already held.</summary>
public static class OperationCoordinator {
    private static readonly HashSet<string> ClaimedPaths = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Lock = new();

    /// <summary>Claims every non-blank path for the caller's run. Throws (claiming nothing) if any of them
    /// is already held by another in-progress run. Dispose the result exactly once, when the run ends, to
    /// release the claim.</summary>
    public static IDisposable Claim(params string?[] outputPaths) {
        var paths = outputPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        lock (Lock) {
            foreach (var path in paths) {
                if (ClaimedPaths.Contains(path))
                    throw new InvalidOperationException(
                        $"Another running operation is already writing to \"{path}\". Wait for it to finish, or change the output path.");
            }
            foreach (var path in paths)
                ClaimedPaths.Add(path);
        }

        return new Release(paths);
    }

    private sealed class Release(List<string> paths) : IDisposable {
        private bool _released;

        public void Dispose() {
            if (_released)
                return;
            _released = true;
            lock (Lock) {
                foreach (var path in paths)
                    ClaimedPaths.Remove(path);
            }
        }
    }
}
