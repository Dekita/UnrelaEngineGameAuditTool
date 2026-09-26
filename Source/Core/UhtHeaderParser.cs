using System.Text.RegularExpressions;

namespace DekUnrealGameAudit.Core;

/// <summary>Parses UE4SS's UHTHeaderDump folder - one `.h` file per reflected type under
/// `&lt;Module&gt;/Public/`, formatted like real UE source (UCLASS/USTRUCT specifiers,
/// UPROPERTY/UFUNCTION declarations). No offsets here (see CxxHeaderParser for that) - this is for
/// semantically meaningful changes: a property's Blueprint exposure, a function's parameters, etc.
/// `&lt;Module&gt;/Private/*.cpp` is just constructor boilerplate and isn't parsed.</summary>
public static partial class UhtHeaderParser {
    [GeneratedRegex(@"^(USTRUCT|UCLASS)\s*\((.*)\)\s*$")]
    private static partial Regex TypeAttributePattern();

    // struct/class Name [: public Parent] { - UCLASS declarations insert a MODULE_API macro before the
    // name (e.g. "class BITREACTORGAME_API UFoo : public UBar {"); USTRUCT ones usually don't.
    [GeneratedRegex(@"^(struct|class)\s+(?:[A-Z_][A-Z0-9_]*_API\s+)?(\w+)(?:\s*:\s*public\s+([\w:]+))?\s*\{\s*$")]
    private static partial Regex TypeDeclarationPattern();

    [GeneratedRegex(@"^(UPROPERTY|UFUNCTION)\s*\((.*)\)\s*$")]
    private static partial Regex MemberAttributePattern();

    public static Dictionary<string, UhtType> Parse(string uhtHeaderDumpFolder, Action<string>? onLog = null,
        CancellationToken cancellationToken = default) {
        var types = new Dictionary<string, UhtType>(StringComparer.Ordinal);

        foreach (var moduleDir in Directory.EnumerateDirectories(uhtHeaderDumpFolder)) {
            cancellationToken.ThrowIfCancellationRequested();
            var publicDir = Path.Combine(moduleDir, "Public");
            if (!Directory.Exists(publicDir))
                continue;

            var moduleName = Path.GetFileName(moduleDir);
            foreach (var file in Directory.EnumerateFiles(publicDir, "*.h", SearchOption.TopDirectoryOnly)) {
                cancellationToken.ThrowIfCancellationRequested();
                UhtType? type;
                try {
                    type = ParseFile(file, moduleName);
                } catch (Exception ex) {
                    if (ex is OperationCanceledException)
                        throw;
                    // Same leniency as CxxHeaderParser - one bad file shouldn't abort the whole scan.
                    onLog?.Invoke($"  WARN: failed to parse '{Path.GetFileName(file)}': {ex.Message}");
                    continue;
                }
                if (type != null && !types.TryAdd(type.Name, type))
                    onLog?.Invoke($"  WARN: duplicate UHT type name '{type.Name}' - keeping the first occurrence.");
            }
        }

        return types;
    }

    private static UhtType? ParseFile(string path, string moduleName) {
        var lines = File.ReadAllLines(path);
        var i = 0;

        string? typeSpecifiers = null;
        while (i < lines.Length) {
            var attrMatch = TypeAttributePattern().Match(lines[i].Trim());
            if (attrMatch.Success) {
                typeSpecifiers = attrMatch.Groups[2].Value;
                i++;
                break;
            }
            i++;
        }
        if (typeSpecifiers == null)
            return null; // not a reflected type in this file (or an unrecognized format) - skip leniently

        Match? declMatch = null;
        while (i < lines.Length) {
            var line = lines[i].Trim();
            if (line.Length > 0) {
                declMatch = TypeDeclarationPattern().Match(line);
                break;
            }
            i++;
        }
        if (declMatch is not { Success: true })
            return null;

        var type = new UhtType {
            Name = declMatch.Groups[2].Value,
            Kind = declMatch.Groups[1].Value,
            Parent = declMatch.Groups[3].Success ? declMatch.Groups[3].Value : null,
            Specifiers = typeSpecifiers,
            Module = moduleName,
        };
        i++;

        // Body: UPROPERTY/UFUNCTION attribute lines are each immediately followed by their declaration -
        // everything else (access specifiers, GENERATED_BODY(), constructors, blank lines) is skipped.
        while (i < lines.Length) {
            var line = lines[i].Trim();
            if (line == "};")
                break;

            var memberMatch = MemberAttributePattern().Match(line);
            if (memberMatch.Success) {
                var kind = memberMatch.Groups[1].Value;
                var specifiers = memberMatch.Groups[2].Value;
                i++;

                while (i < lines.Length && lines[i].Trim().Length == 0)
                    i++;
                if (i >= lines.Length)
                    break;

                var declLine = lines[i].Trim().TrimEnd(';');
                if (kind == "UPROPERTY") {
                    var (propType, propName) = SplitTypeAndName(declLine);
                    if (propName != null)
                        type.Properties.Add(new UhtProperty { Specifiers = specifiers, Type = propType!, Name = propName });
                } else {
                    var (returnType, funcName, parameters, qualifiers) = SplitFunctionSignature(declLine);
                    if (funcName != null)
                        type.Functions.Add(new UhtFunction { Specifiers = specifiers, ReturnType = returnType!, Name = funcName, Parameters = parameters!, Qualifiers = qualifiers! });
                }
            }
            i++;
        }

        return type;
    }

    private static (string? Type, string? Name) SplitTypeAndName(string declLine) {
        var eq = declLine.IndexOf('=');
        if (eq >= 0)
            declLine = declLine[..eq];
        declLine = declLine.Trim();

        var lastSpace = declLine.LastIndexOf(' ');
        if (lastSpace < 0)
            return (null, null);

        // Best-effort: doesn't precisely strip an array-size suffix like "Name[4]" from the name - acceptable
        // since this is a semantic diff aid, not the authoritative layout source (CxxHeaderParser is that).
        return (declLine[..lastSpace].Trim(), declLine[(lastSpace + 1)..].Trim().TrimEnd('[', ']'));
    }

    private static (string? ReturnType, string? Name, string? Parameters, string? Qualifiers) SplitFunctionSignature(string declLine) {
        var openParen = declLine.IndexOf('(');
        if (openParen < 0)
            return (null, null, null, null);

        var beforeParen = declLine[..openParen].Trim();
        var lastSpace = beforeParen.LastIndexOf(' ');
        if (lastSpace < 0)
            return (null, null, null, null);

        // closeParen is the LAST ')' on the line, not the one matching openParen - safe here because a
        // trailing qualifier (const/override/final) never itself contains parens, so the true parameter-list
        // close paren is always the last one. A trailing qualifier's text (previously discarded entirely) is
        // now captured instead of silently disappearing from the diff.
        var closeParen = declLine.LastIndexOf(')');
        var parameters = closeParen > openParen ? declLine[(openParen + 1)..closeParen] : "";
        var qualifiers = closeParen > openParen && closeParen + 1 < declLine.Length ? declLine[(closeParen + 1)..].Trim() : "";
        return (beforeParen[..lastSpace].Trim(), beforeParen[(lastSpace + 1)..].Trim(), parameters, qualifiers);
    }
}
