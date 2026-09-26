using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DekUnrealGameAudit.Core;

// ---- CXX layout side (from UE4SS's CXXHeaderDump - exact memory offsets/sizes) ----

public class CxxField {
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Offset { get; set; } = "";
    public string Size { get; set; } = "";
}

public class CxxType {
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Parent { get; set; }
    public string TotalSize { get; set; } = "";
    public string Module { get; set; } = "";
    public List<CxxField> Fields { get; set; } = new();
    /// <summary>Raw member function declarations - no offset comment exists for these (not layout-relevant),
    /// so only their presence/name/signature text is tracked, not memory position.</summary>
    public List<string> Functions { get; set; } = new();
    /// <summary>Raw lines that clearly have UE4SS's offset/size comment (so they ARE data members) but don't
    /// match the plain "Type Name; // offset (size)" shape FieldLinePattern parses - most likely an array or
    /// bitfield declaration. Not structurally parsed, but the raw text is still compared between two
    /// manifests so a change here isn't silently invisible just because it isn't a tracked Field.</summary>
    public List<string> UnsupportedFieldLines { get; set; } = new();
}

public class CxxEnumValue {
    public string Name { get; set; } = "";
    public long Value { get; set; }
}

public class CxxEnum {
    public string Name { get; set; } = "";
    public string Module { get; set; } = "";
    public List<CxxEnumValue> Values { get; set; } = new();
}

// ---- UHT source side (from UE4SS's UHTHeaderDump - reconstructed, real-source-like) ----

public class UhtProperty {
    public string Specifiers { get; set; } = "";
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
}

public class UhtFunction {
    public string Specifiers { get; set; } = "";
    public string ReturnType { get; set; } = "";
    public string Name { get; set; } = "";
    public string Parameters { get; set; } = "";
    /// <summary>Everything after the closing paren of the parameter list, up to the trailing semicolon
    /// (e.g. "const override", "const"). Previously discarded entirely, which let a qualifier-only change
    /// (e.g. adding `override` or `const`) disappear from the diff even though the declaration changed.</summary>
    public string Qualifiers { get; set; } = "";
}

public class UhtType {
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? Parent { get; set; }
    public string Specifiers { get; set; } = "";
    public string Module { get; set; } = "";
    public List<UhtProperty> Properties { get; set; } = new();
    public List<UhtFunction> Functions { get; set; } = new();
}

/// <summary>One manifest holds both dump types, however many of the two sibling UE4SS dump folders were
/// actually found under the target ue4ss folder.</summary>
public class HeaderManifest {
    public string GeneratedAt { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public Dictionary<string, CxxType> CxxTypes { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, CxxEnum> CxxEnums { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, UhtType> UhtTypes { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Whether the CXXHeaderDump/UHTHeaderDump folder was actually found under the scanned ue4ss
    /// folder (CODE-04) - distinguishes "this section has zero entries because it was never dumped" from
    /// "this section has zero entries because it was dumped and genuinely empty," so HeaderCompareEngine can
    /// tell the two apart instead of comparing an empty dictionary as if it were trustworthy either way.
    /// Defaults to true (not false) so a manifest saved before this field existed - which genuinely lacks this
    /// information - deserializes as "coverage known," preserving exactly the comparison behavior it already
    /// had rather than retroactively flagging historical data as suspect.</summary>
    public bool CxxDumpFound { get; set; } = true;
    public bool UhtDumpFound { get; set; } = true;
}

// ---- Diff result shapes ----

/// <summary>OffsetShifted is the critical "this will read/write the wrong bytes at runtime" signal for
/// native mods. Serialized as its member name (e.g. "OffsetShifted") via StringEnumConverter, matching the
/// plain strings this replaced exactly, so existing header-diff.json output is unchanged.</summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum CxxFieldChangeType { Added, Removed, OffsetShifted, SizeChanged, TypeChanged }

[JsonConverter(typeof(StringEnumConverter))]
public enum CxxEnumValueChangeType { Added, Removed, ValueChanged }

[JsonConverter(typeof(StringEnumConverter))]
public enum UhtMemberChangeType { Added, Removed, Changed }

public class CxxFieldChange {
    public string Field { get; set; } = "";
    public CxxFieldChangeType ChangeType { get; set; }
    public string? OldType { get; set; }
    public string? NewType { get; set; }
    public string? OldOffset { get; set; }
    public string? NewOffset { get; set; }
    public string? OldSize { get; set; }
    public string? NewSize { get; set; }
}

public class CxxTypeChange {
    public string TypeName { get; set; } = "";
    public string? OldTotalSize { get; set; }
    public string? NewTotalSize { get; set; }
    public string? OldParent { get; set; }
    public string? NewParent { get; set; }
    public List<CxxFieldChange> FieldChanges { get; set; } = new();
    /// <summary>Populated only when the set of raw unsupported field-declaration lines (see
    /// <see cref="CxxType.UnsupportedFieldLines"/>) differs between the two scans - an array/bitfield-style
    /// member changed in a way this tool can't structurally describe, but the raw before/after text is shown
    /// rather than the change going undetected.</summary>
    public List<string> OldUnsupportedFieldLines { get; set; } = new();
    public List<string> NewUnsupportedFieldLines { get; set; } = new();
}

public class CxxEnumValueChange {
    public string Name { get; set; } = "";
    public CxxEnumValueChangeType ChangeType { get; set; }
    public long? OldValue { get; set; }
    public long? NewValue { get; set; }
}

public class CxxEnumChange {
    public string EnumName { get; set; } = "";
    public List<CxxEnumValueChange> ValueChanges { get; set; } = new();
}

public class UhtMemberChange {
    public string Member { get; set; } = "";
    public string Kind { get; set; } = ""; // "Property" or "Function"
    public UhtMemberChangeType ChangeType { get; set; }
    public string? OldSignature { get; set; }
    public string? NewSignature { get; set; }
}

public class UhtTypeChange {
    public string TypeName { get; set; } = "";
    public string? OldParent { get; set; }
    public string? NewParent { get; set; }
    /// <summary>Populated only when the UCLASS/USTRUCT(...) specifier text itself changed (e.g. Blueprintable
    /// added/removed) - previously invisible unless a member also changed.</summary>
    public string? OldSpecifiers { get; set; }
    public string? NewSpecifiers { get; set; }
    /// <summary>Populated only when the type changed between "struct" and "class" (or vice versa).</summary>
    public string? OldKind { get; set; }
    public string? NewKind { get; set; }
    public List<UhtMemberChange> MemberChanges { get; set; } = new();
}

public class HeaderCompareResult {
    public string GeneratedAt { get; set; } = "";
    /// <summary>Full resolved paths of the two header manifests actually used - see
    /// GameCompareResult.OldManifestPath for why this is set by the caller, not the engine.</summary>
    public string OldManifestPath { get; set; } = "";
    public string NewManifestPath { get; set; } = "";
    /// <summary>False when either manifest's CxxDumpFound/UhtDumpFound was false - the corresponding
    /// Added/Removed/Changed/Unchanged fields below are then left at their zero defaults rather than computed,
    /// since comparing against a side that was never dumped isn't a meaningful diff (CODE-04). Always check
    /// this before trusting an all-zero result as "confirmed unchanged."</summary>
    public bool CxxCoverageKnown { get; set; } = true;
    public bool UhtCoverageKnown { get; set; } = true;

    public List<string> CxxAddedTypes { get; set; } = new();
    public List<string> CxxRemovedTypes { get; set; } = new();
    public List<CxxTypeChange> CxxChangedTypes { get; set; } = new();
    public int CxxUnchangedTypeCount { get; set; }

    public List<string> CxxAddedEnums { get; set; } = new();
    public List<string> CxxRemovedEnums { get; set; } = new();
    public List<CxxEnumChange> CxxChangedEnums { get; set; } = new();
    public int CxxUnchangedEnumCount { get; set; }

    public List<string> UhtAddedTypes { get; set; } = new();
    public List<string> UhtRemovedTypes { get; set; } = new();
    public List<UhtTypeChange> UhtChangedTypes { get; set; } = new();
    public int UhtUnchangedTypeCount { get; set; }
}
