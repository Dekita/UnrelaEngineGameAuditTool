using System.IO;
using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Gui;

/// <summary>The pieces of Run_Click/RetrySave_Click that were byte-for-byte identical across
/// HashGameFilesView/HashModFilesView/HashHeaderFilesView - the rest of TrySaveManifest's success path has
/// real per-view differences (different manifest types, different post-success UI) that don't unify this
/// cleanly, so only what was actually duplicated is extracted here.</summary>
internal static class OperationUiHelpers {
    public static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.Hours > 0 ? elapsed.ToString(@"h\:mm\:ss") : elapsed.ToString(@"m\:ss");

    /// <summary>Creates the output directory (if any) and claims the path via OperationCoordinator, logging
    /// and returning null on either failure - collapses the "preflight + claim + log-and-bail" block that was
    /// duplicated 6 times (Run_Click and RetrySave_Click across 3 files) to one call each. Matches the
    /// existing "auto-named output is never claimed" convention: pass null/blank for an auto-named path.</summary>
    public static IDisposable? TryClaimOutput(string outPathInput, Action<string> log) {
        if (!string.IsNullOrWhiteSpace(outPathInput)) {
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPathInput))!);
            } catch (Exception ex) {
                log($"Output file path is invalid: {ex.Message}");
                return null;
            }
        }

        try {
            return OperationCoordinator.Claim(outPathInput);
        } catch (Exception ex) {
            log(ex.Message);
            return null;
        }
    }
}
