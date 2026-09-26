using CUE4Parse.FileProvider;
using UE4Config.Parsing;

namespace DekUnrealGameAudit.Core;

/// <summary>Best-effort detection of a game's own version string, for suffixing manifest filenames so two
/// scans of different game builds are easy to tell apart later. Not every game populates this meaningfully -
/// callers should always allow a manual override rather than relying on this alone.</summary>
public static class GameVersionDetector {
    public static string? TryDetect(DefaultFileProvider provider, Action<string>? onLog = null) =>
        TryDetect(provider.DefaultGame, onLog);

    /// <summary>Split out from the DefaultFileProvider-based overload above purely so this is unit-testable
    /// directly against a real (but not provider-mounted) CustomConfigIni instance - DefaultFileProvider itself
    /// can't be constructed in a test. Never throws (INV-05): "best-effort" wasn't actually guarded against a
    /// null/misbehaving DefaultGame.ini before this, so any failure here now returns null - with the reason
    /// told to onLog rather than swallowed silently - instead of aborting the whole hash run. Deliberately
    /// doesn't catch OperationCanceledException - a real cancellation must still propagate, not be reported as
    /// just a version-detection failure.</summary>
    internal static string? TryDetect(CustomConfigIni? defaultGame, Action<string>? onLog = null) {
        if (defaultGame == null)
            return null;

        try {
            var instructions = new List<InstructionToken>();
            defaultGame.FindPropertyInstructions(
                "/Script/EngineSettings.GeneralProjectSettings", "ProjectVersion", instructions);

            if (instructions.Count == 0 || string.IsNullOrWhiteSpace(instructions[0].Value))
                return null;

            return Sanitize(instructions[0].Value);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            onLog?.Invoke($"  WARN: could not detect game version from DefaultGame.ini ({ex.Message}) - continuing without it.");
            return null;
        }
    }

    /// <summary>Strips characters that aren't safe in a filename, for embedding a detected/user-supplied
    /// version string directly into a generated manifest name.</summary>
    public static string Sanitize(string value) {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray();
        return new string(chars);
    }
}
