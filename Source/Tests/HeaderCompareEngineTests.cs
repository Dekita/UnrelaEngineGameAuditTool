using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

public class HeaderCompareEngineTests {
    private static CxxType MakeType(string name, params CxxField[] fields) =>
        new() { Name = name, Kind = "struct", TotalSize = "0x10", Fields = fields.ToList() };

    private static CxxField Field(string type, string name, string offset, string size) =>
        new() { Type = type, Name = name, Offset = offset, Size = size };

    [Fact]
    public void Diff_FieldOffsetShift_IsFlaggedAsOffsetShifted() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0000", "0x4"));
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0008", "0x4"));

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var change = Assert.Single(result.CxxChangedTypes);
        var fieldChange = Assert.Single(change.FieldChanges);
        Assert.Equal(CxxFieldChangeType.OffsetShifted, fieldChange.ChangeType);
        Assert.Equal("0x0000", fieldChange.OldOffset);
        Assert.Equal("0x0008", fieldChange.NewOffset);
    }

    [Fact]
    public void Diff_FieldSizeChangeWithoutOffsetShift_IsFlaggedAsSizeChanged() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("uint8", "Flag", "0x0000", "0x1"));
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("uint8", "Flag", "0x0000", "0x4"));

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var fieldChange = Assert.Single(Assert.Single(result.CxxChangedTypes).FieldChanges);
        Assert.Equal(CxxFieldChangeType.SizeChanged, fieldChange.ChangeType);
    }

    [Fact]
    public void Diff_AddedAndRemovedFields_AreReported() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Removed", "0x0000", "0x4"));
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Added", "0x0000", "0x4"));

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var change = Assert.Single(result.CxxChangedTypes);
        Assert.Contains(change.FieldChanges, f => f.Field == "Added" && f.ChangeType == CxxFieldChangeType.Added);
        Assert.Contains(change.FieldChanges, f => f.Field == "Removed" && f.ChangeType == CxxFieldChangeType.Removed);
    }

    [Fact]
    public void Diff_IdenticalTypes_ProduceNoChangesAndCountAsUnchanged() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0000", "0x4"));
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0000", "0x4"));

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        Assert.Empty(result.CxxChangedTypes);
        Assert.Equal(1, result.CxxUnchangedTypeCount);
    }

    [Fact]
    public void Diff_EnumValueChange_IsFlaggedAsValueChanged() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxEnums["ETest"] = new CxxEnum { Name = "ETest", Values = { new CxxEnumValue { Name = "A", Value = 0 } } };
        var newManifest = new HeaderManifest();
        newManifest.CxxEnums["ETest"] = new CxxEnum { Name = "ETest", Values = { new CxxEnumValue { Name = "A", Value = 1 } } };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var valueChange = Assert.Single(Assert.Single(result.CxxChangedEnums).ValueChanges);
        Assert.Equal(CxxEnumValueChangeType.ValueChanged, valueChange.ChangeType);
        Assert.Equal(0, valueChange.OldValue);
        Assert.Equal(1, valueChange.NewValue);
    }

    [Fact]
    public void Diff_UhtPropertySpecifierChange_IsReportedAsChanged() {
        var oldManifest = new HeaderManifest();
        oldManifest.UhtTypes["UFoo"] = new UhtType {
            Name = "UFoo",
            Properties = { new UhtProperty { Specifiers = "BlueprintReadOnly", Type = "int32", Name = "Health" } },
        };
        var newManifest = new HeaderManifest();
        newManifest.UhtTypes["UFoo"] = new UhtType {
            Name = "UFoo",
            Properties = { new UhtProperty { Specifiers = "BlueprintReadWrite", Type = "int32", Name = "Health" } },
        };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var memberChange = Assert.Single(Assert.Single(result.UhtChangedTypes).MemberChanges);
        Assert.Equal(UhtMemberChangeType.Changed, memberChange.ChangeType);
        Assert.Equal("Property", memberChange.Kind);
    }

    [Fact]
    public void Diff_ChangeTypeSerializesAsPlainEnumNameString() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0000", "0x4"));
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0008", "0x4"));

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(result);

        Assert.Contains("\"OffsetShifted\"", json);
    }

    [Fact]
    public void Diff_AddedAndRemovedTypes_AreReportedByName() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FGone"] = MakeType("FGone");
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FNew"] = MakeType("FNew");

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        Assert.Equal(["FNew"], result.CxxAddedTypes);
        Assert.Equal(["FGone"], result.CxxRemovedTypes);
    }

    // CODE-09: DiffUhtType previously compared members/parent but ignored the type-level
    // UCLASS/USTRUCT(...) specifiers and struct/class Kind entirely.
    [Fact]
    public void Diff_UhtTypeSpecifierOnlyChange_IsReportedEvenWithNoMemberChange() {
        var oldManifest = new HeaderManifest();
        oldManifest.UhtTypes["UFoo"] = new UhtType { Name = "UFoo", Kind = "class", Specifiers = "Blueprintable" };
        var newManifest = new HeaderManifest();
        newManifest.UhtTypes["UFoo"] = new UhtType { Name = "UFoo", Kind = "class", Specifiers = "Blueprintable, BlueprintType" };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var change = Assert.Single(result.UhtChangedTypes);
        Assert.Equal("Blueprintable", change.OldSpecifiers);
        Assert.Equal("Blueprintable, BlueprintType", change.NewSpecifiers);
    }

    [Fact]
    public void Diff_UhtTypeKindChange_IsReportedEvenWithNoMemberChange() {
        var oldManifest = new HeaderManifest();
        oldManifest.UhtTypes["FFoo"] = new UhtType { Name = "FFoo", Kind = "struct" };
        var newManifest = new HeaderManifest();
        newManifest.UhtTypes["FFoo"] = new UhtType { Name = "FFoo", Kind = "class" };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var change = Assert.Single(result.UhtChangedTypes);
        Assert.Equal("struct", change.OldKind);
        Assert.Equal("class", change.NewKind);
    }

    [Fact]
    public void Diff_IdenticalUhtSpecifiersAndKind_ProducesNoChange() {
        var oldManifest = new HeaderManifest();
        oldManifest.UhtTypes["UFoo"] = new UhtType { Name = "UFoo", Kind = "class", Specifiers = "Blueprintable" };
        var newManifest = new HeaderManifest();
        newManifest.UhtTypes["UFoo"] = new UhtType { Name = "UFoo", Kind = "class", Specifiers = "Blueprintable" };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        Assert.Empty(result.UhtChangedTypes);
        Assert.Equal(1, result.UhtUnchangedTypeCount);
    }

    // CODE-09: a trailing qualifier (const/override/etc) was discarded entirely by the UHT function parser,
    // so a qualifier-only change was invisible to the diff.
    [Fact]
    public void Diff_UhtFunctionQualifierOnlyChange_IsReportedAsChanged() {
        var oldManifest = new HeaderManifest();
        oldManifest.UhtTypes["UFoo"] = new UhtType {
            Name = "UFoo",
            Functions = { new UhtFunction { ReturnType = "bool", Name = "CanDoThing", Parameters = "", Qualifiers = "" } },
        };
        var newManifest = new HeaderManifest();
        newManifest.UhtTypes["UFoo"] = new UhtType {
            Name = "UFoo",
            Functions = { new UhtFunction { ReturnType = "bool", Name = "CanDoThing", Parameters = "", Qualifiers = "const" } },
        };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var memberChange = Assert.Single(Assert.Single(result.UhtChangedTypes).MemberChanges);
        Assert.Equal("Function", memberChange.Kind);
        Assert.Equal(UhtMemberChangeType.Changed, memberChange.ChangeType);
    }

    // CODE-09: offsets/sizes were compared as raw strings, so "0x10" vs "0x010" (or different casing) was
    // reported as a change despite being numerically identical.
    [Theory]
    [InlineData("0x10", "0x010")]
    [InlineData("0x1A", "0x1a")]
    [InlineData("0x0000", "0x0")]
    public void Diff_EquivalentHexSpellings_AreNotReportedAsChanged(string oldHex, string newHex) {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", oldHex, "0x4"));
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", newHex, "0x4"));

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        Assert.Empty(result.CxxChangedTypes);
        Assert.Equal(1, result.CxxUnchangedTypeCount);
    }

    [Fact]
    public void Diff_EquivalentTotalSizeSpellings_AreNotReportedAsChanged() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = new CxxType { Name = "FFoo", Kind = "struct", TotalSize = "0x10" };
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = new CxxType { Name = "FFoo", Kind = "struct", TotalSize = "0x010" };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        Assert.Empty(result.CxxChangedTypes);
    }

    [Fact]
    public void Diff_GenuinelyDifferentHexValues_AreStillReportedAsChanged() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0000", "0x4"));
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0004", "0x4"));

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        Assert.Single(result.CxxChangedTypes);
    }

    // CODE-09: array/bitfield-style declarations aren't structurally parsed into Fields, but a change in
    // their raw text must still surface rather than silently vanishing (esp. when TotalSize is unchanged).
    [Fact]
    public void Diff_UnsupportedFieldLineChange_IsReportedEvenWhenTotalSizeIsUnchanged() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = new CxxType {
            Name = "FFoo", Kind = "struct", TotalSize = "0x10",
            UnsupportedFieldLines = { "uint8 Values[4]; // 0x0000 (size: 0x4)" },
        };
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = new CxxType {
            Name = "FFoo", Kind = "struct", TotalSize = "0x10",
            UnsupportedFieldLines = { "uint8 Values[8]; // 0x0000 (size: 0x8)" },
        };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        var change = Assert.Single(result.CxxChangedTypes);
        Assert.Contains("uint8 Values[4]; // 0x0000 (size: 0x4)", change.OldUnsupportedFieldLines);
        Assert.Contains("uint8 Values[8]; // 0x0000 (size: 0x8)", change.NewUnsupportedFieldLines);
    }

    // CODE-10 acceptance: "a removed-only enum report names the enum" - RenderMarkdown previously had no
    // section for CxxAddedEnums/CxxRemovedEnums at all (not even a count).
    [Fact]
    public void RenderMarkdown_ARemovedOnlyEnumReport_NamesTheEnum() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxEnums["EGone"] = new CxxEnum { Name = "EGone" };
        var newManifest = new HeaderManifest();

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);
        var markdown = HeaderCompareEngine.RenderMarkdown(result);

        Assert.Contains("EGone", markdown);
    }

    [Fact]
    public void RenderMarkdown_ListsAddedAndRemovedTypeNames_NotJustCounts() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FGoneType"] = MakeType("FGoneType");
        oldManifest.UhtTypes["UGoneType"] = new UhtType { Name = "UGoneType" };
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FNewType"] = MakeType("FNewType");
        newManifest.UhtTypes["UNewType"] = new UhtType { Name = "UNewType" };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);
        var markdown = HeaderCompareEngine.RenderMarkdown(result);

        Assert.Contains("FGoneType", markdown);
        Assert.Contains("FNewType", markdown);
        Assert.Contains("UGoneType", markdown);
        Assert.Contains("UNewType", markdown);
    }

    // CODE-10 acceptance: "function changes show signatures" - previously only "~ Function Name" with no
    // indication of what actually changed about it.
    [Fact]
    public void RenderMarkdown_UhtFunctionChange_ShowsOldAndNewSignatures() {
        var oldManifest = new HeaderManifest();
        oldManifest.UhtTypes["UFoo"] = new UhtType {
            Name = "UFoo",
            Functions = { new UhtFunction { ReturnType = "bool", Name = "DoThing", Parameters = "int32 A" } },
        };
        var newManifest = new HeaderManifest();
        newManifest.UhtTypes["UFoo"] = new UhtType {
            Name = "UFoo",
            Functions = { new UhtFunction { ReturnType = "bool", Name = "DoThing", Parameters = "int32 A, int32 B" } },
        };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);
        var markdown = HeaderCompareEngine.RenderMarkdown(result);

        Assert.Contains("int32 A)", markdown);
        Assert.Contains("int32 A, int32 B)", markdown);
    }

    // CODE-10 acceptance: "combined field changes show all differences" - a field whose offset AND type both
    // changed at once previously only showed the offset move, silently dropping the type change from view.
    [Fact]
    public void RenderMarkdown_FieldWithOffsetAndTypeChange_ShowsBothNotJustOne() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("int32", "Health", "0x0000", "0x4"));
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo", Field("float", "Health", "0x0008", "0x4"));

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);
        var markdown = HeaderCompareEngine.RenderMarkdown(result);
        var description = HeaderCompareEngine.DescribeFieldChange(Assert.Single(Assert.Single(result.CxxChangedTypes).FieldChanges));

        Assert.Contains("moved", description);
        Assert.Contains("type", description);
        Assert.Contains("int32", markdown);
        Assert.Contains("float", markdown);
        Assert.Contains("0x0000", markdown);
        Assert.Contains("0x0008", markdown);
    }

    [Fact]
    public void DescribeFieldChange_AddedField_DoesNotClaimAMove() {
        var change = new CxxFieldChange { Field = "New", ChangeType = CxxFieldChangeType.Added, NewType = "int32", NewOffset = "0x0", NewSize = "0x4" };

        Assert.Equal("added", HeaderCompareEngine.DescribeFieldChange(change));
    }

    // CODE-10 acceptance: special characters in names/paths must not corrupt the Markdown table.
    [Fact]
    public void RenderMarkdown_TypeNameContainingPipeAndBacktick_DoesNotBreakTheTable() {
        var oldManifest = new HeaderManifest();
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FWeird|Name`Here"] = MakeType("FWeird|Name`Here");

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);
        var markdown = HeaderCompareEngine.RenderMarkdown(result);

        Assert.Contains("FWeird\\|Name'Here", markdown);
    }

    [Fact]
    public void Diff_IdenticalUnsupportedFieldLines_ProduceNoChange() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = new CxxType {
            Name = "FFoo", Kind = "struct", TotalSize = "0x10",
            UnsupportedFieldLines = { "uint8 Values[4]; // 0x0000 (size: 0x4)" },
        };
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = new CxxType {
            Name = "FFoo", Kind = "struct", TotalSize = "0x10",
            UnsupportedFieldLines = { "uint8 Values[4]; // 0x0000 (size: 0x4)" },
        };

        var result = HeaderCompareEngine.Compare(oldManifest, newManifest);

        Assert.Empty(result.CxxChangedTypes);
        Assert.Equal(1, result.CxxUnchangedTypeCount);
    }

    // CODE-04 (header-coverage half): a manifest whose CXXHeaderDump folder was never found must not be
    // compared as if its empty CxxTypes were trustworthy - the real bug this guards against is exactly this:
    // the old side (missing the dump) has nothing, so every real type on the new side would otherwise show up
    // as "Added," which is a misleading mass-addition, not a real one.
    [Fact]
    public void Compare_OldSideMissingCxxDump_DoesNotReportNewSideTypesAsAdded() {
        var oldManifest = new HeaderManifest { CxxDumpFound = false };
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo");
        newManifest.CxxTypes["FBar"] = MakeType("FBar");

        var warnings = new List<string>();
        var result = HeaderCompareEngine.Compare(oldManifest, newManifest, onLog: warnings.Add);

        Assert.False(result.CxxCoverageKnown);
        Assert.Empty(result.CxxAddedTypes);
        Assert.Empty(result.CxxRemovedTypes);
        Assert.Empty(result.CxxChangedTypes);
        Assert.Equal(0, result.CxxUnchangedTypeCount);
        Assert.Contains(warnings, w => w.Contains("CXXHeaderDump", StringComparison.OrdinalIgnoreCase));
    }

    // The other failure mode from the plan's own evidence: two incomplete scans (both missing the dump) must
    // not silently look like a clean "0 changes" - that's indistinguishable from a real, confirmed no-op
    // comparison unless the coverage flag/warning says otherwise.
    [Fact]
    public void Compare_BothSidesMissingUhtDump_FlagsUnknownCoverageInsteadOfSilentZeroChanges() {
        var oldManifest = new HeaderManifest { UhtDumpFound = false };
        var newManifest = new HeaderManifest { UhtDumpFound = false };

        var warnings = new List<string>();
        var result = HeaderCompareEngine.Compare(oldManifest, newManifest, onLog: warnings.Add);

        Assert.False(result.UhtCoverageKnown);
        Assert.Equal(0, result.UhtUnchangedTypeCount);
        Assert.Contains(warnings, w => w.Contains("UHTHeaderDump", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Compare_BothSidesHaveDumps_CoverageIsKnownAndNoWarningFires() {
        var oldManifest = new HeaderManifest();
        oldManifest.CxxTypes["FFoo"] = MakeType("FFoo");
        var newManifest = new HeaderManifest();
        newManifest.CxxTypes["FFoo"] = MakeType("FFoo");

        var warnings = new List<string>();
        var result = HeaderCompareEngine.Compare(oldManifest, newManifest, onLog: warnings.Add);

        Assert.True(result.CxxCoverageKnown);
        Assert.True(result.UhtCoverageKnown);
        Assert.Empty(warnings);
        Assert.Equal(1, result.CxxUnchangedTypeCount);
    }

    // Backward compatibility: a manifest JSON shaped like one saved before CxxDumpFound/UhtDumpFound existed
    // (the fields simply absent) must deserialize as coverage-known, preserving exactly the comparison
    // behavior it already had - not retroactively flagged as suspect just because the field is new.
    [Fact]
    public void OldManifestJsonMissingCoverageFields_DeserializesAsCoverageKnown() {
        var json = """{"generatedAt":"2026-01-01","sourceFolder":"x","cxxTypes":{},"cxxEnums":{},"uhtTypes":{}}""";
        var manifest = Newtonsoft.Json.JsonConvert.DeserializeObject<HeaderManifest>(json)!;

        Assert.True(manifest.CxxDumpFound);
        Assert.True(manifest.UhtDumpFound);
    }
}
