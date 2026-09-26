using System.Text;

namespace DekUnrealGameAudit.Core;

/// <summary>Compares two HeaderManifests: an independent CXX layout pass (memory-breaking changes) and an
/// independent UHT source pass (semantic changes), combined into one result. Shared by the CLI and the GUI.</summary>
public static class HeaderCompareEngine {
    public static HeaderCompareResult Compare(HeaderManifest oldManifest, HeaderManifest newManifest,
        Action<string>? onLog = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new HeaderCompareResult { GeneratedAt = DateTime.UtcNow.ToString("o") };

        // CODE-04: comparing against a side that never had this dump folder isn't a meaningful diff - it
        // would either misreport the other side's entire real content as "Added" (one side missing) or
        // silently report "0 changes" (both sides missing, which looks identical to a genuinely clean result).
        // Skipping the diff entirely and flagging coverage as unknown is the same always-surface-explicitly
        // choice already made for the game-asset half of this same item, applied consistently here.
        result.CxxCoverageKnown = oldManifest.CxxDumpFound && newManifest.CxxDumpFound;
        result.UhtCoverageKnown = oldManifest.UhtDumpFound && newManifest.UhtDumpFound;

        if (!result.CxxCoverageKnown)
            onLog?.Invoke($"WARN: CXXHeaderDump wasn't found in {CoverageSideDescription(oldManifest.CxxDumpFound, newManifest.CxxDumpFound)} - memory-layout comparison is not meaningful and was skipped.");
        if (!result.UhtCoverageKnown)
            onLog?.Invoke($"WARN: UHTHeaderDump wasn't found in {CoverageSideDescription(oldManifest.UhtDumpFound, newManifest.UhtDumpFound)} - source/reflection comparison is not meaningful and was skipped.");

        if (result.CxxCoverageKnown) {
            foreach (var (name, newType) in newManifest.CxxTypes) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!oldManifest.CxxTypes.TryGetValue(name, out var oldType)) {
                    result.CxxAddedTypes.Add(name);
                    continue;
                }
                var change = DiffCxxType(oldType, newType);
                if (change != null) result.CxxChangedTypes.Add(change);
                else result.CxxUnchangedTypeCount++;
            }
            foreach (var name in oldManifest.CxxTypes.Keys) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!newManifest.CxxTypes.ContainsKey(name))
                    result.CxxRemovedTypes.Add(name);
            }

            foreach (var (name, newEnum) in newManifest.CxxEnums) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!oldManifest.CxxEnums.TryGetValue(name, out var oldEnum)) {
                    result.CxxAddedEnums.Add(name);
                    continue;
                }
                var change = DiffCxxEnum(oldEnum, newEnum);
                if (change != null) result.CxxChangedEnums.Add(change);
                else result.CxxUnchangedEnumCount++;
            }
            foreach (var name in oldManifest.CxxEnums.Keys) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!newManifest.CxxEnums.ContainsKey(name))
                    result.CxxRemovedEnums.Add(name);
            }
        }

        if (result.UhtCoverageKnown) {
            foreach (var (name, newType) in newManifest.UhtTypes) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!oldManifest.UhtTypes.TryGetValue(name, out var oldType)) {
                    result.UhtAddedTypes.Add(name);
                    continue;
                }
                var change = DiffUhtType(oldType, newType);
                if (change != null) result.UhtChangedTypes.Add(change);
                else result.UhtUnchangedTypeCount++;
            }
            foreach (var name in oldManifest.UhtTypes.Keys) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!newManifest.UhtTypes.ContainsKey(name))
                    result.UhtRemovedTypes.Add(name);
            }
        }

        result.CxxAddedTypes.Sort(StringComparer.Ordinal);
        result.CxxRemovedTypes.Sort(StringComparer.Ordinal);
        result.CxxChangedTypes.Sort((a, b) => string.CompareOrdinal(a.TypeName, b.TypeName));
        result.CxxAddedEnums.Sort(StringComparer.Ordinal);
        result.CxxRemovedEnums.Sort(StringComparer.Ordinal);
        result.CxxChangedEnums.Sort((a, b) => string.CompareOrdinal(a.EnumName, b.EnumName));
        result.UhtAddedTypes.Sort(StringComparer.Ordinal);
        result.UhtRemovedTypes.Sort(StringComparer.Ordinal);
        result.UhtChangedTypes.Sort((a, b) => string.CompareOrdinal(a.TypeName, b.TypeName));

        return result;
    }

    private static string CoverageSideDescription(bool oldFound, bool newFound) => (oldFound, newFound) switch {
        (false, false) => "either scan",
        (false, true) => "the old scan",
        (true, false) => "the new scan",
        _ => "either scan", // unreachable when called (both true means coverage IS known) - exhaustive for the compiler
    };

    /// <summary>ToDictionary keyed by name, but tolerant of duplicate names within the same member list
    /// (e.g. overloaded functions sharing a name) - keeps the first occurrence rather than throwing.</summary>
    private static Dictionary<string, T> ByName<T>(IEnumerable<T> items, Func<T, string> nameOf) {
        var dict = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in items)
            dict.TryAdd(nameOf(item), item);
        return dict;
    }

    /// <summary>Compares two "0xNNNN"-style hex strings by numeric value, not spelling - "0x10" and "0x010"
    /// (or different-case digits) are the same offset/size and must not be reported as changed just because
    /// UE4SS happened to print them differently between two dumps. Falls back to an ordinal string compare
    /// for anything that doesn't parse as hex, so a genuinely unexpected value still counts as different
    /// rather than being silently treated as equal.</summary>
    private static bool HexValuesEqual(string a, string b) {
        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;
        var aValue = TryParseHex(a);
        var bValue = TryParseHex(b);
        return aValue != null && bValue != null && aValue == bValue;
    }

    private static long? TryParseHex(string value) =>
        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
        long.TryParse(value.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var parsed)
            ? parsed
            : null;

    private static CxxTypeChange? DiffCxxType(CxxType oldType, CxxType newType) {
        var oldFields = ByName(oldType.Fields, f => f.Name);
        var newFields = ByName(newType.Fields, f => f.Name);
        var allNames = new HashSet<string>(oldFields.Keys, StringComparer.Ordinal);
        allNames.UnionWith(newFields.Keys);

        var fieldChanges = new List<CxxFieldChange>();
        foreach (var name in allNames) {
            oldFields.TryGetValue(name, out var oldField);
            newFields.TryGetValue(name, out var newField);
            var change = DiffField(oldField, newField);
            if (change != null)
                fieldChanges.Add(change);
        }

        var sizeChanged = !HexValuesEqual(oldType.TotalSize, newType.TotalSize);
        var parentChanged = oldType.Parent != newType.Parent;
        // Raw, un-parsed array/bitfield declaration text - compared as sets (not ordered) since UE4SS emits
        // these in consistent declaration order and reordering isn't a meaningful signal here. A difference
        // means an unsupported field changed in some way this tool can't structurally describe - reported so
        // it isn't a silent gap, per CODE-09's evidence about array/bitfield changes being missable.
        var oldUnsupported = new HashSet<string>(oldType.UnsupportedFieldLines, StringComparer.Ordinal);
        var newUnsupported = new HashSet<string>(newType.UnsupportedFieldLines, StringComparer.Ordinal);
        var unsupportedChanged = !oldUnsupported.SetEquals(newUnsupported);

        if (fieldChanges.Count == 0 && !sizeChanged && !parentChanged && !unsupportedChanged)
            return null;

        return new CxxTypeChange {
            TypeName = newType.Name,
            OldTotalSize = sizeChanged ? oldType.TotalSize : null,
            NewTotalSize = sizeChanged ? newType.TotalSize : null,
            OldParent = parentChanged ? oldType.Parent : null,
            NewParent = parentChanged ? newType.Parent : null,
            FieldChanges = fieldChanges.OrderBy(f => f.Field, StringComparer.Ordinal).ToList(),
            OldUnsupportedFieldLines = unsupportedChanged ? oldType.UnsupportedFieldLines : new(),
            NewUnsupportedFieldLines = unsupportedChanged ? newType.UnsupportedFieldLines : new(),
        };
    }

    private static CxxFieldChange? DiffField(CxxField? oldField, CxxField? newField) {
        if (oldField == null && newField == null)
            return null;
        if (oldField == null)
            return new CxxFieldChange { Field = newField!.Name, ChangeType = CxxFieldChangeType.Added, NewType = newField.Type, NewOffset = newField.Offset, NewSize = newField.Size };
        if (newField == null)
            return new CxxFieldChange { Field = oldField.Name, ChangeType = CxxFieldChangeType.Removed, OldType = oldField.Type, OldOffset = oldField.Offset, OldSize = oldField.Size };

        var offsetChanged = !HexValuesEqual(oldField.Offset, newField.Offset);
        var sizeChanged = !HexValuesEqual(oldField.Size, newField.Size);
        var typeChanged = oldField.Type != newField.Type;
        if (!offsetChanged && !sizeChanged && !typeChanged)
            return null;

        // OffsetShifted takes priority as the label - it's the "this will read/write the wrong bytes at
        // runtime" signal - but old/new values are populated for every property that actually differs.
        return new CxxFieldChange {
            Field = newField.Name,
            ChangeType = offsetChanged ? CxxFieldChangeType.OffsetShifted : sizeChanged ? CxxFieldChangeType.SizeChanged : CxxFieldChangeType.TypeChanged,
            OldType = typeChanged ? oldField.Type : null,
            NewType = typeChanged ? newField.Type : null,
            OldOffset = offsetChanged ? oldField.Offset : null,
            NewOffset = offsetChanged ? newField.Offset : null,
            OldSize = sizeChanged ? oldField.Size : null,
            NewSize = sizeChanged ? newField.Size : null,
        };
    }

    private static CxxEnumChange? DiffCxxEnum(CxxEnum oldEnum, CxxEnum newEnum) {
        var oldValues = ByName(oldEnum.Values, v => v.Name);
        var newValues = ByName(newEnum.Values, v => v.Name);
        var allNames = new HashSet<string>(oldValues.Keys, StringComparer.Ordinal);
        allNames.UnionWith(newValues.Keys);

        var valueChanges = new List<CxxEnumValueChange>();
        foreach (var name in allNames) {
            var hasOld = oldValues.TryGetValue(name, out var oldValue);
            var hasNew = newValues.TryGetValue(name, out var newValue);
            if (hasOld && hasNew) {
                if (oldValue!.Value != newValue!.Value)
                    valueChanges.Add(new CxxEnumValueChange { Name = name, ChangeType = CxxEnumValueChangeType.ValueChanged, OldValue = oldValue.Value, NewValue = newValue.Value });
            } else if (hasNew) {
                valueChanges.Add(new CxxEnumValueChange { Name = name, ChangeType = CxxEnumValueChangeType.Added, NewValue = newValue!.Value });
            } else {
                valueChanges.Add(new CxxEnumValueChange { Name = name, ChangeType = CxxEnumValueChangeType.Removed, OldValue = oldValue!.Value });
            }
        }

        if (valueChanges.Count == 0)
            return null;
        return new CxxEnumChange { EnumName = newEnum.Name, ValueChanges = valueChanges.OrderBy(v => v.Name, StringComparer.Ordinal).ToList() };
    }

    private static UhtTypeChange? DiffUhtType(UhtType oldType, UhtType newType) {
        var memberChanges = new List<UhtMemberChange>();

        DiffMembers(
            ByName(oldType.Properties, p => p.Name),
            ByName(newType.Properties, p => p.Name),
            "Property", p => $"{p.Specifiers} | {p.Type}", memberChanges);

        DiffMembers(
            ByName(oldType.Functions, f => f.Name),
            ByName(newType.Functions, f => f.Name),
            "Function", f => $"{f.Specifiers} | {f.ReturnType} {f.Name}({f.Parameters}) {f.Qualifiers}", memberChanges);

        var parentChanged = oldType.Parent != newType.Parent;
        // A UCLASS/USTRUCT-only specifier change (e.g. Blueprintable added/removed) or a struct<->class Kind
        // change previously left the whole type looking unchanged if no member also changed.
        var specifiersChanged = oldType.Specifiers != newType.Specifiers;
        var kindChanged = oldType.Kind != newType.Kind;
        if (memberChanges.Count == 0 && !parentChanged && !specifiersChanged && !kindChanged)
            return null;

        return new UhtTypeChange {
            TypeName = newType.Name,
            OldParent = parentChanged ? oldType.Parent : null,
            NewParent = parentChanged ? newType.Parent : null,
            OldSpecifiers = specifiersChanged ? oldType.Specifiers : null,
            NewSpecifiers = specifiersChanged ? newType.Specifiers : null,
            OldKind = kindChanged ? oldType.Kind : null,
            NewKind = kindChanged ? newType.Kind : null,
            MemberChanges = memberChanges.OrderBy(m => m.Member, StringComparer.Ordinal).ToList(),
        };
    }

    private static void DiffMembers<T>(
        Dictionary<string, T> oldMembers, Dictionary<string, T> newMembers,
        string kind, Func<T, string> signatureOf, List<UhtMemberChange> results) {
        var allNames = new HashSet<string>(oldMembers.Keys, StringComparer.Ordinal);
        allNames.UnionWith(newMembers.Keys);

        foreach (var name in allNames) {
            var hasOld = oldMembers.TryGetValue(name, out var oldMember);
            var hasNew = newMembers.TryGetValue(name, out var newMember);
            if (hasOld && hasNew) {
                var oldSignature = signatureOf(oldMember!);
                var newSignature = signatureOf(newMember!);
                if (oldSignature != newSignature)
                    results.Add(new UhtMemberChange { Member = name, Kind = kind, ChangeType = UhtMemberChangeType.Changed, OldSignature = oldSignature, NewSignature = newSignature });
            } else if (hasNew) {
                results.Add(new UhtMemberChange { Member = name, Kind = kind, ChangeType = UhtMemberChangeType.Added, NewSignature = signatureOf(newMember!) });
            } else {
                results.Add(new UhtMemberChange { Member = name, Kind = kind, ChangeType = UhtMemberChangeType.Removed, OldSignature = signatureOf(oldMember!) });
            }
        }
    }

    /// <summary>Plain-text (no Markdown markup) description of everything that changed about one field,
    /// combining offset/size/type together when more than one differs at once - the model already records
    /// every dimension that changed (see DiffField), this just stops the renderer from picking only one of
    /// them to show via ChangeType's single label. Shared by the Markdown renderer and the GUI's log view so
    /// the two don't drift into describing the same change differently.</summary>
    public static string DescribeFieldChange(CxxFieldChange fc) {
        if (fc.ChangeType is CxxFieldChangeType.Added or CxxFieldChangeType.Removed)
            return fc.ChangeType == CxxFieldChangeType.Added ? "added" : "removed";

        var parts = new List<string>();
        if (fc.OldOffset != null || fc.NewOffset != null) parts.Add($"moved {fc.OldOffset} -> {fc.NewOffset}");
        if (fc.OldSize != null || fc.NewSize != null) parts.Add($"size {fc.OldSize} -> {fc.NewSize}");
        if (fc.OldType != null || fc.NewType != null) parts.Add($"type {fc.OldType} -> {fc.NewType}");
        return string.Join(", ", parts);
    }

    public static string RenderMarkdown(HeaderCompareResult result) {
        var sb = new StringBuilder();
        sb.AppendLine("# Header/SDK diff");
        sb.AppendLine();
        sb.AppendLine($"Generated: {result.GeneratedAt}");
        sb.AppendLine();

        sb.AppendLine("## Memory layout (CXXHeaderDump) - breaking changes");
        sb.AppendLine();
        if (!result.CxxCoverageKnown) {
            sb.AppendLine("⚠ **Coverage unknown** - CXXHeaderDump wasn't found on one or both sides, so the " +
                "counts below are not a real comparison, not confirmation nothing changed.");
            sb.AppendLine();
        }
        sb.AppendLine($"Added: **{result.CxxAddedTypes.Count}**, Removed: **{result.CxxRemovedTypes.Count}**, " +
            $"Changed: **{result.CxxChangedTypes.Count}**, Unchanged: **{result.CxxUnchangedTypeCount}**");
        sb.AppendLine();

        AppendNameList(sb, "Added types", result.CxxAddedTypes);
        AppendNameList(sb, "Removed types", result.CxxRemovedTypes);

        if (result.CxxChangedTypes.Count > 0) {
            sb.AppendLine("| Type | Change |");
            sb.AppendLine("|---|---|");
            foreach (var typeChange in result.CxxChangedTypes) {
                var details = new List<string>();
                if (typeChange.OldTotalSize != null)
                    details.Add($"size {typeChange.OldTotalSize} -> {typeChange.NewTotalSize}");
                if (typeChange.OldParent != null || typeChange.NewParent != null)
                    details.Add($"parent {MarkdownEscape.Cell(typeChange.OldParent ?? "(none)")} -> {MarkdownEscape.Cell(typeChange.NewParent ?? "(none)")}");
                foreach (var fc in typeChange.FieldChanges) {
                    var field = MarkdownEscape.Cell(fc.Field);
                    details.Add(fc.ChangeType switch {
                        CxxFieldChangeType.Added => $"+ `{field}`",
                        CxxFieldChangeType.Removed => $"- `{field}`",
                        CxxFieldChangeType.OffsetShifted => $"⚠ `{field}` {MarkdownEscape.Cell(DescribeFieldChange(fc))}",
                        _ => $"`{field}` {MarkdownEscape.Cell(DescribeFieldChange(fc))}",
                    });
                }
                if (typeChange.OldUnsupportedFieldLines.Count > 0 || typeChange.NewUnsupportedFieldLines.Count > 0) {
                    details.Add("⚠ unsupported field declaration(s) changed (not structurally diffed):");
                    foreach (var line in typeChange.OldUnsupportedFieldLines) details.Add($"  - old: `{MarkdownEscape.Cell(line.Trim())}`");
                    foreach (var line in typeChange.NewUnsupportedFieldLines) details.Add($"  - new: `{MarkdownEscape.Cell(line.Trim())}`");
                }
                sb.AppendLine($"| {MarkdownEscape.Cell(typeChange.TypeName)} | {string.Join("<br>", details)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"Enums - Added: **{result.CxxAddedEnums.Count}**, Removed: **{result.CxxRemovedEnums.Count}**, " +
            $"Changed: **{result.CxxChangedEnums.Count}**, Unchanged: **{result.CxxUnchangedEnumCount}**");
        sb.AppendLine();
        AppendNameList(sb, "Added enums", result.CxxAddedEnums);
        AppendNameList(sb, "Removed enums", result.CxxRemovedEnums);

        if (result.CxxChangedEnums.Count > 0) {
            sb.AppendLine("### Enum value changes");
            sb.AppendLine();
            sb.AppendLine("| Enum | Change |");
            sb.AppendLine("|---|---|");
            foreach (var enumChange in result.CxxChangedEnums) {
                var details = enumChange.ValueChanges.Select(vc => vc.ChangeType switch {
                    CxxEnumValueChangeType.ValueChanged => $"⚠ `{MarkdownEscape.Cell(vc.Name)}` {vc.OldValue} -> {vc.NewValue}",
                    CxxEnumValueChangeType.Added => $"+ `{MarkdownEscape.Cell(vc.Name)}` = {vc.NewValue}",
                    CxxEnumValueChangeType.Removed => $"- `{MarkdownEscape.Cell(vc.Name)}` (was {vc.OldValue})",
                    _ => MarkdownEscape.Cell(vc.Name),
                });
                sb.AppendLine($"| {MarkdownEscape.Cell(enumChange.EnumName)} | {string.Join("<br>", details)} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Source/reflection (UHTHeaderDump) - semantic changes");
        sb.AppendLine();
        if (!result.UhtCoverageKnown) {
            sb.AppendLine("⚠ **Coverage unknown** - UHTHeaderDump wasn't found on one or both sides, so the " +
                "counts below are not a real comparison, not confirmation nothing changed.");
            sb.AppendLine();
        }
        sb.AppendLine($"Added: **{result.UhtAddedTypes.Count}**, Removed: **{result.UhtRemovedTypes.Count}**, " +
            $"Changed: **{result.UhtChangedTypes.Count}**, Unchanged: **{result.UhtUnchangedTypeCount}**");
        sb.AppendLine();

        AppendNameList(sb, "Added types", result.UhtAddedTypes);
        AppendNameList(sb, "Removed types", result.UhtRemovedTypes);

        if (result.UhtChangedTypes.Count > 0) {
            sb.AppendLine("| Type | Change |");
            sb.AppendLine("|---|---|");
            foreach (var typeChange in result.UhtChangedTypes) {
                var details = new List<string>();
                if (typeChange.OldParent != null || typeChange.NewParent != null)
                    details.Add($"parent {MarkdownEscape.Cell(typeChange.OldParent ?? "(none)")} -> {MarkdownEscape.Cell(typeChange.NewParent ?? "(none)")}");
                if (typeChange.OldKind != null || typeChange.NewKind != null)
                    details.Add($"⚠ kind {MarkdownEscape.Cell(typeChange.OldKind)} -> {MarkdownEscape.Cell(typeChange.NewKind)}");
                if (typeChange.OldSpecifiers != null || typeChange.NewSpecifiers != null)
                    details.Add($"specifiers `{MarkdownEscape.Cell(typeChange.OldSpecifiers)}` -> `{MarkdownEscape.Cell(typeChange.NewSpecifiers)}`");
                foreach (var mc in typeChange.MemberChanges) {
                    var member = MarkdownEscape.Cell(mc.Member);
                    details.Add(mc.ChangeType switch {
                        UhtMemberChangeType.Added => $"+ {mc.Kind} `{member}` = `{MarkdownEscape.Cell(mc.NewSignature)}`",
                        UhtMemberChangeType.Removed => $"- {mc.Kind} `{member}` (was `{MarkdownEscape.Cell(mc.OldSignature)}`)",
                        _ => $"~ {mc.Kind} `{member}`: `{MarkdownEscape.Cell(mc.OldSignature)}` -> `{MarkdownEscape.Cell(mc.NewSignature)}`",
                    });
                }
                sb.AppendLine($"| {MarkdownEscape.Cell(typeChange.TypeName)} | {string.Join("<br>", details)} |");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static void AppendNameList(StringBuilder sb, string label, List<string> names) {
        if (names.Count == 0)
            return;
        sb.AppendLine($"**{label} ({names.Count}):** " + string.Join(", ", names.Select(n => $"`{MarkdownEscape.Cell(n)}`")));
        sb.AppendLine();
    }
}
