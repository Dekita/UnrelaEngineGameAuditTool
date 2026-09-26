using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Gui;

/// <summary>Loads and validates a scope-rules file for a GUI view before it starts a run - shared by every
/// view offering a "Scope rules" field, so an invalid file is always caught before the UI disables itself and
/// a scan starts, not partway through. Blank input returns null, which is the entire CODE-16 backward-
/// compatibility guarantee: no file, no scope filtering, unchanged behavior.</summary>
public static class ScopeRulesLoader {
    public static ScopeRuleSet? Load(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        var ruleSet = JsonUtil.ReadFile<ScopeRuleSet>(path);
        ScopeClassifier.Validate(ruleSet);
        return ruleSet;
    }
}
