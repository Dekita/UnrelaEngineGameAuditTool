namespace DekUnrealGameAudit.Core;

/// <summary>Rejects an output path that would silently overwrite one of the inputs it was computed from, or
/// overwrite another output from the same run (e.g. --out and --md pointed at the same file) - checked before
/// any output is written, so a typo never destroys the file you're trying to compare. Paths are resolved to
/// their full form and compared case-insensitively (NTFS is case-insensitive by default), which catches the
/// same path, a relative/absolute spelling of it, and Windows case variants. It does <b>not</b> detect two
/// different path strings that alias the same file via a hard link or reparse point/junction - that would need
/// actual filesystem identity checks, not string comparison, and is intentionally out of scope here.</summary>
public static class PathCollisionGuard {
    /// <summary>Throws if any output path (by full, case-insensitive path) matches an input path or another
    /// output path. Blank/null paths are ignored (an unset optional output has nothing to collide with).</summary>
    public static void CheckNoCollisions(
        IEnumerable<(string Label, string? Path)> inputs,
        IEnumerable<(string Label, string? Path)> outputs) {

        var seenInputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, path) in inputs) {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            seenInputs[Path.GetFullPath(path)] = label;
        }

        var seenOutputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, path) in outputs) {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var full = Path.GetFullPath(path);

            if (seenInputs.TryGetValue(full, out var inputLabel))
                throw new InvalidOperationException(
                    $"Refusing to run: {label} ('{path}') would overwrite {inputLabel}, one of the inputs it's computed from.");

            if (seenOutputs.TryGetValue(full, out var otherOutputLabel))
                throw new InvalidOperationException(
                    $"Refusing to run: {label} and {otherOutputLabel} would both write to the same file ('{path}').");

            seenOutputs[full] = label;
        }
    }
}
