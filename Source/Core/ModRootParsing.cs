namespace DekUnrealGameAudit.Core;

/// <summary>Parses the Mods folder text (GUI multi-line box, or CLI repeated `--mods` values joined the same
/// way) into one-or-more root folders - mirrors <see cref="AesKeyParsing"/>'s "one value per line" shape so
/// <see cref="ScanOptions.TargetFolder"/> stays a plain string with no settings/profile schema change: a
/// single-line value behaves exactly as it always has, and a multi-line value is CODE-17's deferred "true
/// multi-root scanning" - several separately configured mod roots scanned and merged into one run.</summary>
public static class ModRootParsing {
    /// <summary>Splits on newlines, trims, drops blank lines, resolves each to a full path (matching how a
    /// single mod root has always been resolved before use), and removes exact duplicates - pasting the same
    /// folder twice (or the same folder two different ways) collapses to one entry. This does NOT detect
    /// *overlapping* roots (one nested inside another) - that's handled downstream in <see cref="ModsHasher"/>
    /// by de-duplicating the containers actually discovered under each root, since two different-looking root
    /// paths can still resolve to overlapping content on disk.</summary>
    public static List<string> ParseModRoots(string? text) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        foreach (var raw in (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            string full;
            try {
                full = Path.GetFullPath(raw);
            } catch {
                full = raw;
            }
            if (seen.Add(full))
                roots.Add(full);
        }
        return roots;
    }
}
