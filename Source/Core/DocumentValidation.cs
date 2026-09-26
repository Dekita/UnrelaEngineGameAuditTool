using Newtonsoft.Json.Linq;

namespace DekUnrealGameAudit.Core;

/// <summary>Thrown when a JSON file's top-level shape doesn't match the document kind it was read as - e.g. a
/// mods manifest fed into a game-manifest reader. Newtonsoft's default lenient deserialization would otherwise
/// silently leave the wrong-shaped properties at their default (empty) values instead of failing: two mods
/// manifests read as game manifests would compare as "no differences", and an unrelated file read as a
/// comparison could make real mods look unaffected by an update. Always names the file and the document kind
/// that was expected, never just a generic parse error.</summary>
public class DocumentValidationException : Exception {
    public DocumentValidationException(string message) : base(message) { }
}

/// <summary>Per-document-type required top-level shape, checked against the raw JSON before deserializing -
/// deserialization alone can't distinguish "empty because this manifest is genuinely empty" from "empty because
/// this key isn't in the file at all, since it's the wrong kind of file". Only covers the document kinds ever
/// read back in as another command's input (<see cref="GameManifest"/>, <see cref="ModManifest"/>,
/// <see cref="GameCompareResult"/>, <see cref="HeaderManifest"/>, <see cref="ScopeRuleSet"/>) -
/// <see cref="ModsCompareReport"/> and
/// <see cref="HeaderCompareResult"/> are only ever written, never read, so there is nothing to validate on the
/// way in for those. Deliberately checks presence/type only, not per-entry content - a fully malformed
/// individual entry (e.g. a hash value of the wrong JSON type) is a separate, smaller concern from the "this is
/// the wrong document entirely" failure mode this targets.</summary>
internal static class DocumentShape {
    private static readonly Dictionary<Type, (string Kind, (string Property, JTokenType Type)[] Required)> Shapes = new() {
        [typeof(GameManifest)] = ("game manifest", [("ueVersion", JTokenType.String), ("assets", JTokenType.Object)]),
        [typeof(ModManifest)] = ("mods manifest", [("mods", JTokenType.Object)]),
        [typeof(GameCompareResult)] = ("game comparison", [
            ("added", JTokenType.Array), ("removed", JTokenType.Array), ("changed", JTokenType.Array)]),
        [typeof(HeaderManifest)] = ("header manifest", [
            ("cxxTypes", JTokenType.Object), ("cxxEnums", JTokenType.Object), ("uhtTypes", JTokenType.Object)]),
        [typeof(ScopeRuleSet)] = ("scope rules file", [("rules", JTokenType.Array)]),
    };

    public static void Validate<T>(JObject root, string path) {
        if (!Shapes.TryGetValue(typeof(T), out var shape))
            return;

        foreach (var (property, expectedType) in shape.Required) {
            var token = root.GetValue(property, StringComparison.OrdinalIgnoreCase);
            if (token == null)
                throw new DocumentValidationException(
                    $"'{path}' does not look like a {shape.Kind} - it's missing the required '{property}' " +
                    "property. Check this is the right kind of file for what you're comparing.");

            if (token.Type != expectedType)
                throw new DocumentValidationException(
                    $"'{path}' does not look like a {shape.Kind} - '{property}' should be " +
                    $"{Describe(expectedType)}, but found {Describe(token.Type)}.");
        }
    }

    private static string Describe(JTokenType type) => type switch {
        JTokenType.Object => "an object",
        JTokenType.Array => "an array",
        JTokenType.String => "a string",
        _ => type.ToString(),
    };
}
