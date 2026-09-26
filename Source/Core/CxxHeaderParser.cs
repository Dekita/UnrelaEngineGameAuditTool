using System.Text.RegularExpressions;

namespace DekUnrealGameAudit.Core;

/// <summary>Parses UE4SS's CXXHeaderDump folder - flat `.hpp` files (some per-module aggregates holding
/// hundreds of blocks, some per-Blueprint-class single-type files - same block shape either way) plus
/// `&lt;Module&gt;_enums.hpp` files. Every data member carries its exact byte offset/size as a trailing
/// comment; this is the source of truth for detecting memory-layout-breaking changes.</summary>
public static partial class CxxHeaderParser {
    // struct/class Name [: public Parent] { <body> }; // Size: 0xTOTAL
    [GeneratedRegex(
        @"^(struct|class)\s+(\w+)(?:\s*:\s*public\s+([\w:]+))?\s*\r?\n\{\r?\n(.*?)\r?\n\};\s*//\s*Size:\s*(0x[0-9A-Fa-f]+)",
        RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex TypeBlockPattern();

    // Type Name; // 0xOFFSET (size: 0xSIZE) - greedy type capture so the split lands on the LAST space
    // before the identifier (handles pointers/templates like "class UFoo* Bar;" or "TArray<X> Bar;").
    [GeneratedRegex(@"^(.+)\s+(\w+);\s*//\s*(0x[0-9A-Fa-f]+)\s*\(size:\s*(0x[0-9A-Fa-f]+)\)\s*$")]
    private static partial Regex FieldLinePattern();

    // namespace Name { enum Type { <body> }; }
    [GeneratedRegex(@"namespace\s+(\w+)\s*\{\s*enum\s+Type\s*\{(.*?)\}\s*;\s*\}", RegexOptions.Singleline)]
    private static partial Regex EnumBlockPattern();

    [GeneratedRegex(@"^(\w+)\s*=\s*(-?\d+)\s*,?\s*$")]
    private static partial Regex EnumValueLinePattern();

    public static (Dictionary<string, CxxType> Types, Dictionary<string, CxxEnum> Enums) Parse(
        string cxxHeaderDumpFolder, Action<string>? onLog = null,
        CancellationToken cancellationToken = default) {
        var types = new Dictionary<string, CxxType>(StringComparer.Ordinal);
        var enums = new Dictionary<string, CxxEnum>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(cxxHeaderDumpFolder, "*.hpp", SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();
            var moduleName = Path.GetFileNameWithoutExtension(file);
            try {
                var text = File.ReadAllText(file);
                if (moduleName.EndsWith("_enums", StringComparison.OrdinalIgnoreCase)) {
                    ParseEnumFile(text, moduleName, enums, onLog);
                } else {
                    ParseTypeFile(text, moduleName, types, onLog);
                }
            } catch (Exception ex) {
                if (ex is OperationCanceledException)
                    throw;
                // A single unreadable/malformed file (locked by another process, permissions, a stray
                // non-UTF8 byte) shouldn't abort parsing the other several thousand - same "skip and keep
                // going" leniency as everything else in this parser.
                onLog?.Invoke($"  WARN: failed to parse '{Path.GetFileName(file)}': {ex.Message}");
            }
        }

        return (types, enums);
    }

    private static void ParseTypeFile(string text, string moduleName, Dictionary<string, CxxType> types, Action<string>? onLog) {
        foreach (Match match in TypeBlockPattern().Matches(text)) {
            var name = match.Groups[2].Value;
            var type = new CxxType {
                Name = name,
                Kind = match.Groups[1].Value,
                Parent = match.Groups[3].Success ? match.Groups[3].Value : null,
                TotalSize = match.Groups[5].Value,
                Module = moduleName,
            };

            foreach (var rawLine in match.Groups[4].Value.Split('\n')) {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0)
                    continue;

                // A bitfield line like "uint8 bFlag : 1;" technically satisfies FieldLinePattern's greedy
                // "(.+)\s+(\w+);" shape too - "1" matches \w+ as a bogus "name" with "uint8 bFlag :" as its
                // "type". A real field name can never start with a digit, so reject that case explicitly
                // rather than silently recording a nonsense field.
                var fieldMatch = FieldLinePattern().Match(line);
                var isBitfieldMisparse = fieldMatch.Success && char.IsDigit(fieldMatch.Groups[2].Value[0]);
                if (fieldMatch.Success && !isBitfieldMisparse) {
                    type.Fields.Add(new CxxField {
                        Type = fieldMatch.Groups[1].Value.Trim(),
                        Name = fieldMatch.Groups[2].Value,
                        Offset = fieldMatch.Groups[3].Value,
                        Size = fieldMatch.Groups[4].Value,
                    });
                } else if (line.EndsWith(';')) {
                    // No offset comment - a member function declaration, not layout-relevant. Tracked by
                    // raw text only (presence/signature), not position.
                    type.Functions.Add(line);
                } else if (line.Contains("// 0x", StringComparison.Ordinal) && line.Contains("(size:", StringComparison.Ordinal)) {
                    // Has an offset/size comment (so this IS a data member UE4SS emitted layout info for),
                    // but not one FieldLinePattern's plain "Type Name;" shape matches - an array
                    // ("Name[4];") or bitfield ("Name : 1;") declaration, most likely. Silently dropping
                    // this would make its offset/size invisible to the diff instead of just unsupported;
                    // surfacing it as a named coverage gap is the honest alternative until a real dump
                    // sample is available to build a verified regex against (see CODE-09 notes).
                    type.UnsupportedFieldLines.Add(line);
                    onLog?.Invoke($"  WARN: unsupported field declaration in '{name}' ({moduleName}.hpp) - not diffed: {line}");
                }
                // Anything else (unrecognized generated syntax with no offset comment at all) is skipped -
                // this is generated text from a tool we don't control, and a handful of unparsed lines
                // shouldn't fail the scan.
            }

            if (!types.TryAdd(name, type))
                onLog?.Invoke($"  WARN: duplicate CXX type name '{name}' (in {moduleName}.hpp) - keeping the first occurrence.");
        }
    }

    private static void ParseEnumFile(string text, string moduleName, Dictionary<string, CxxEnum> enums, Action<string>? onLog) {
        foreach (Match match in EnumBlockPattern().Matches(text)) {
            var name = match.Groups[1].Value;
            var cxxEnum = new CxxEnum { Name = name, Module = moduleName };

            foreach (var rawLine in match.Groups[2].Value.Split('\n')) {
                var line = rawLine.TrimEnd('\r').Trim();
                if (line.Length == 0)
                    continue;

                var valueMatch = EnumValueLinePattern().Match(line);
                if (valueMatch.Success) {
                    cxxEnum.Values.Add(new CxxEnumValue {
                        Name = valueMatch.Groups[1].Value,
                        Value = long.Parse(valueMatch.Groups[2].Value),
                    });
                }
            }

            if (!enums.TryAdd(name, cxxEnum))
                onLog?.Invoke($"  WARN: duplicate CXX enum name '{name}' (in {moduleName}.hpp) - keeping the first occurrence.");
        }
    }
}
