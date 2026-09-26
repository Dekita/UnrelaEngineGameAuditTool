using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

/// <summary>Shared `--scope-rules <file.json>` handling for hash-game-files/hash-mod-files - loads and
/// validates before any mounting happens, matching CODE-13's "validate before mounting" precedent. Returns null
/// when the flag wasn't given at all, which is the entire CODE-16 backward-compatibility guarantee: no flag,
/// no behavior change.</summary>
internal static class ScopeRulesArg {
    public static ScopeRuleSet? Load(ArgMap flags) {
        var path = flags.OneOrDefault("scope-rules");
        if (path == null)
            return null;

        var ruleSet = JsonUtil.ReadFile<ScopeRuleSet>(path);
        ScopeClassifier.Validate(ruleSet);
        return ruleSet;
    }
}
