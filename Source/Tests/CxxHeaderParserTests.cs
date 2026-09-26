using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Fixture text below mirrors UE4SS's real CXXHeaderDump format exactly (verified against a real
/// dump during development) - offset/size trailing comments, a "// Size: 0xTOTAL" block terminator, and
/// member functions with no offset comment.</summary>
public class CxxHeaderParserTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory("cxx-header-parser-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteHpp(string fileName, string content) => File.WriteAllText(Path.Combine(_dir, fileName), content);

    [Fact]
    public void Parse_ExtractsFieldsWithOffsetsAndSizesAndFunctions() {
        WriteHpp("TestModule.hpp", """
            struct FAIMimicGroupCooldown
            {
            	int32 CooldownGroup;                                              // 0x0000 (size: 0x4)
            	uint8 TriggeredAfterNumUsages;                                    // 0x0004 (size: 0x1)
            }; // Size: 0xC

            class AAIPlanner : public AActor
            {
            	TArray<FPlannerTeamCoupleRelation> TeamRelations;                 // 0x02B0 (size: 0x10)
            	void SimulatePlan();
            }; // Size: 0x860

            """);

        var (types, enums) = CxxHeaderParser.Parse(_dir);

        Assert.Empty(enums);
        var cooldown = types["FAIMimicGroupCooldown"];
        Assert.Equal("struct", cooldown.Kind);
        Assert.Null(cooldown.Parent);
        Assert.Equal("0xC", cooldown.TotalSize);
        Assert.Equal(2, cooldown.Fields.Count);
        Assert.Equal(("int32", "CooldownGroup", "0x0000", "0x4"), (cooldown.Fields[0].Type, cooldown.Fields[0].Name, cooldown.Fields[0].Offset, cooldown.Fields[0].Size));

        var planner = types["AAIPlanner"];
        Assert.Equal("AActor", planner.Parent);
        Assert.Equal("TArray<FPlannerTeamCoupleRelation>", planner.Fields[0].Type);
        Assert.Equal("0x02B0", planner.Fields[0].Offset);
        Assert.Contains("void SimulatePlan();", planner.Functions);
    }

    [Fact]
    public void Parse_DuplicateTypeName_KeepsFirstAndWarns() {
        WriteHpp("ModuleA.hpp", "struct Dup\n{\n\tint32 FromA;                                              // 0x0000 (size: 0x4)\n}; // Size: 0x4\n");
        WriteHpp("ModuleB.hpp", "struct Dup\n{\n\tint32 FromB;                                              // 0x0000 (size: 0x4)\n}; // Size: 0x4\n");

        var warnings = new List<string>();
        var (types, _) = CxxHeaderParser.Parse(_dir, warnings.Add);

        Assert.Single(types);
        Assert.Contains(warnings, w => w.Contains("duplicate CXX type name"));
    }

    [Fact]
    public void Parse_UnreadableFile_IsSkippedWithWarning_OtherFilesStillParse() {
        WriteHpp("Good.hpp", "struct FGood\n{\n\tint32 Health;                                              // 0x0000 (size: 0x4)\n}; // Size: 0x4\n");
        WriteHpp("Locked.hpp", "struct FLocked\n{\n};  // Size: 0x0\n");

        var warnings = new List<string>();
        // Holding an exclusive lock reliably reproduces "file can't be read right now" without
        // depending on OS-specific permission tricks.
        using (new FileStream(Path.Combine(_dir, "Locked.hpp"), FileMode.Open, FileAccess.Read, FileShare.None)) {
            var (types, _) = CxxHeaderParser.Parse(_dir, warnings.Add);

            Assert.True(types.ContainsKey("FGood"));
            Assert.False(types.ContainsKey("FLocked"));
            Assert.Contains(warnings, w => w.Contains("Locked.hpp"));
        }
    }

    // CODE-09: a data-member line with an offset/size comment that doesn't match the plain "Type Name;"
    // shape (most likely an array or bitfield) was previously silently dropped with no trace at all.
    // Surfacing it as a named diagnostic satisfies "unsupported syntax yields coverage diagnostics"
    // without guessing at a regex for a format not yet verified against a real dump.
    [Fact]
    public void Parse_ArrayLikeFieldLine_IsTrackedAsUnsupportedAndWarns() {
        WriteHpp("TestModule.hpp", """
            struct FFoo
            {
            	uint8 Values[4];                                                  // 0x0000 (size: 0x4)
            }; // Size: 0x4

            """);

        var warnings = new List<string>();
        var (types, _) = CxxHeaderParser.Parse(_dir, warnings.Add);

        var foo = types["FFoo"];
        Assert.Empty(foo.Fields);
        Assert.Single(foo.UnsupportedFieldLines);
        Assert.Contains(warnings, w => w.Contains("unsupported field declaration") && w.Contains("FFoo"));
    }

    [Fact]
    public void Parse_BitfieldLikeFieldLine_IsTrackedAsUnsupportedAndWarns() {
        WriteHpp("TestModule.hpp", """
            struct FFoo
            {
            	uint8 bFlag : 1;                                                  // 0x0000 (size: 0x1)
            }; // Size: 0x1

            """);

        var warnings = new List<string>();
        var (types, _) = CxxHeaderParser.Parse(_dir, warnings.Add);

        var foo = types["FFoo"];
        Assert.Empty(foo.Fields);
        Assert.Single(foo.UnsupportedFieldLines);
        Assert.Contains(warnings, w => w.Contains("unsupported field declaration"));
    }

    [Fact]
    public void Parse_EnumFile_ExtractsNamesAndNumericValues() {
        WriteHpp("TestModule_enums.hpp", """
            namespace ETestEnum
            {
            	enum Type
            	{
            		First = 0,
            		Second = 1,
            	};
            }

            """);

        var (types, enums) = CxxHeaderParser.Parse(_dir);

        Assert.Empty(types);
        var testEnum = enums["ETestEnum"];
        Assert.Equal(2, testEnum.Values.Count);
        Assert.Equal(0, testEnum.Values[0].Value);
        Assert.Equal("Second", testEnum.Values[1].Name);
        Assert.Equal(1, testEnum.Values[1].Value);
    }
}
