namespace DekUnrealGameAudit.Core;

/// <summary>Classifies physical containers under a scan root (e.g. the Paks folder) as Game/Mod/Ignore, per
/// CODE-16 (see IMPROVEMENT-PLAN.md). Default heuristic: a container directly at the root is Game; a container
/// in any descendant folder is Mod. Explicit <see cref="ScopeRule"/>s override that, with precedence: Ignore
/// wins outright (an ignored folder can't be un-ignored by a more specific rule underneath it - narrow or
/// remove the ignore rule instead); otherwise an exact file rule beats a folder rule; among folder rules, the
/// deepest (most specific) one wins; otherwise the default heuristic applies.</summary>
public static class ScopeClassifier {
    /// <summary>Checked once when a rule set is loaded (not per-classification) - rejects paths that would
    /// escape the scan root and rules that contradict each other for the exact same path.</summary>
    public static void Validate(ScopeRuleSet ruleSet) {
        var seen = new Dictionary<(string Path, bool IsFolder), ScopeAction>();
        foreach (var rule in ruleSet.Rules) {
            if (string.IsNullOrWhiteSpace(rule.Path))
                throw new ArgumentException("A scope rule has a blank path.");

            var segments = Segments(rule.Path);
            if (segments.Length == 0)
                throw new ArgumentException($"Scope rule path '{rule.Path}' is empty after normalization.");
            if (segments.Any(s => s is ".." or "."))
                throw new ArgumentException($"Scope rule path '{rule.Path}' escapes the scan root (contains '.' or '..').");

            var key = (string.Join('/', segments).ToLowerInvariant(), rule.IsFolder);
            if (seen.TryGetValue(key, out var existingAction) && existingAction != rule.Action)
                throw new ArgumentException(
                    $"Conflicting scope rules for '{rule.Path}' ({(rule.IsFolder ? "folder" : "file")}): " +
                    $"{existingAction} vs {rule.Action}.");
            seen[key] = rule.Action;
        }
    }

    /// <summary>Classifies one container given its path relative to the scan root, e.g. "~mods/Foo/Foo_P.pak"
    /// or "OfficialDLC/dlc_p.pak".</summary>
    public static ScopeAction Classify(string rootRelativePath, ScopeRuleSet ruleSet) =>
        ClassifyWithReason(rootRelativePath, ruleSet).Action;

    /// <summary>Same result as <see cref="Classify"/>, plus a human-readable explanation of which rule (or
    /// which branch of the default heuristic) produced it - used by UI-10's scope-scan preview so a user can
    /// see *why* a container landed where it did, not just where. Exactly one implementation of the precedence
    /// logic backs both methods, so the two can never drift apart.</summary>
    public static ClassificationReason ClassifyWithReason(string rootRelativePath, ScopeRuleSet ruleSet) {
        var segments = Segments(rootRelativePath);

        ScopeRule? fileMatch = null;
        ScopeRule? deepestFolderMatch = null;
        var deepestFolderDepth = -1;
        ScopeRule? folderIgnoreMatch = null;

        foreach (var rule in ruleSet.Rules) {
            var ruleSegments = Segments(rule.Path);

            if (!rule.IsFolder) {
                if (SegmentsEqual(ruleSegments, segments))
                    fileMatch = rule;
                continue;
            }

            if (!IsAncestorOf(ruleSegments, segments))
                continue;

            if (rule.Action == ScopeAction.Ignore)
                folderIgnoreMatch ??= rule;

            if (ruleSegments.Length > deepestFolderDepth) {
                deepestFolderDepth = ruleSegments.Length;
                deepestFolderMatch = rule;
            }
        }

        // Ignore wins outright - checked ahead of "exact file overrides folder" and "deepest folder wins", so
        // an ignored ancestor folder can never be overridden by a more specific rule underneath it.
        if (fileMatch?.Action == ScopeAction.Ignore)
            return new ClassificationReason(ScopeAction.Ignore, $"Ignored by file rule '{fileMatch.Path}'.");
        if (folderIgnoreMatch != null)
            return new ClassificationReason(ScopeAction.Ignore, $"Ignored by folder rule '{folderIgnoreMatch.Path}'.");
        if (fileMatch != null)
            return new ClassificationReason(fileMatch.Action, $"Included as {fileMatch.Action} by file rule '{fileMatch.Path}'.");
        if (deepestFolderMatch != null)
            return new ClassificationReason(deepestFolderMatch.Action, $"Included as {deepestFolderMatch.Action} by folder rule '{deepestFolderMatch.Path}'.");

        return segments.Length <= 1
            ? new ClassificationReason(ScopeAction.Game, "Default: file directly in the scan root -> Game.")
            : new ClassificationReason(ScopeAction.Mod, "Default: file in a subfolder -> Mod.");
    }

    /// <summary>A stable identifier for a rule set's actual content (order-independent), embedded in manifests
    /// so a future compare step can tell two manifests used different (or unknown/legacy) scope without
    /// treating a filter change as if it were a real game/mod addition or removal.</summary>
    public static string ComputeFingerprint(ScopeRuleSet ruleSet) {
        var canonical = ruleSet.Rules
            .Select(r => $"{string.Join('/', Segments(r.Path)).ToLowerInvariant()}|{r.IsFolder}|{r.Action}")
            .OrderBy(s => s, StringComparer.Ordinal);
        var joined = $"v{ruleSet.Version}:" + string.Join('\n', canonical);
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexStringLower(hash);
    }

    private static bool IsAncestorOf(string[] folderSegments, string[] targetSegments) =>
        targetSegments.Length > folderSegments.Length &&
        folderSegments.Select((seg, i) => string.Equals(seg, targetSegments[i], StringComparison.OrdinalIgnoreCase)).All(eq => eq);

    private static bool SegmentsEqual(string[] a, string[] b) =>
        a.Length == b.Length && a.Zip(b, (x, y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase)).All(eq => eq);

    private static string[] Segments(string path) =>
        path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
}
