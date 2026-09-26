using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DekUnrealGameAudit.Core;

/// <summary>What a scope rule (or the classifier's own default heuristic) resolves a physical container to.
/// Game/Mod feed Hash Game Files/Hash Mod Files respectively; Ignore excludes it from both.</summary>
public enum ScopeAction { Game, Mod, Ignore }

/// <summary>One override, matched against a physical path relative to a scan root (e.g. the Paks folder) -
/// see <see cref="ScopeClassifier"/> for how a set of these combines with the built-in default heuristic.</summary>
public class ScopeRule {
    /// <summary>Root-relative path, e.g. "OfficialDLC" or "~mods/MyMod/MyMod_P.pak". Forward or back slashes,
    /// any casing - normalized before matching.</summary>
    public required string Path { get; init; }

    /// <summary>True for a folder rule (matches every container inside it, at any depth); false for a rule
    /// naming one exact container file.</summary>
    public required bool IsFolder { get; init; }

    [JsonConverter(typeof(StringEnumConverter))]
    public required ScopeAction Action { get; init; }
}

/// <summary>The JSON shape of a `--scope-rules` file: a versioned, ordered-doesn't-matter list of overrides on
/// top of <see cref="ScopeClassifier"/>'s default heuristic. Embedding <see cref="Version"/> now costs nothing
/// and avoids ever having to guess a schema version for an old rules file later.</summary>
public class ScopeRuleSet {
    public int Version { get; init; } = 1;
    public List<ScopeRule> Rules { get; init; } = new();
}

/// <summary>A non-fatal finding from scanning/classifying a scan root - e.g. an IoStore container missing its
/// required companion file. Surfaced through the caller's onLog, never silently dropped.</summary>
public record ScopeDiagnostic(string Path, string Message);

/// <summary>One physical container (a .pak, or a .utoc+.ucas pair identified by its .utoc path) found under a
/// scan root, with its resolved classification.</summary>
public record ClassifiedContainer(string AbsolutePath, string RootRelativePath, ScopeAction Classification);

/// <summary>A classification result paired with a human-readable explanation of which rule (or which branch of
/// the default heuristic) produced it - see <see cref="ScopeClassifier.ClassifyWithReason"/>.</summary>
public record ClassificationReason(ScopeAction Action, string Explanation);
