using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Fixture text mirrors UE4SS's real UHTHeaderDump layout: one &lt;Module&gt;/Public/&lt;Type&gt;.h
/// file per reflected type, UCLASS/USTRUCT + UPROPERTY/UFUNCTION attribute lines each immediately followed
/// by their declaration, "{" on the same line as the type declaration.</summary>
public class UhtHeaderParserTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory("uht-header-parser-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteHeader(string moduleName, string typeFileName, string content) {
        var publicDir = Path.Combine(_dir, moduleName, "Public");
        Directory.CreateDirectory(publicDir);
        File.WriteAllText(Path.Combine(publicDir, typeFileName), content);
    }

    [Fact]
    public void Parse_ExtractsClassWithParentPropertyAndFunction() {
        WriteHeader("TestModule", "UFoo.h",
            "#pragma once\n" +
            "\n" +
            "UCLASS(Blueprintable)\n" +
            "class TESTMODULE_API UFoo : public UObject {\n" +
            "public:\n" +
            "\tUPROPERTY(BlueprintReadWrite, EditAnywhere, Category = \"Foo\")\n" +
            "\tint32 Health;\n" +
            "\n" +
            "\tUFUNCTION(BlueprintCallable, Category = \"Foo\")\n" +
            "\tvoid TakeDamage(int32 Amount);\n" +
            "};\n");

        var types = UhtHeaderParser.Parse(_dir);

        var foo = types["UFoo"];
        Assert.Equal("class", foo.Kind);
        Assert.Equal("UObject", foo.Parent);
        Assert.Equal("Blueprintable", foo.Specifiers);
        Assert.Equal("TestModule", foo.Module);

        var prop = Assert.Single(foo.Properties);
        Assert.Equal("Health", prop.Name);
        Assert.Equal("int32", prop.Type);
        Assert.Contains("BlueprintReadWrite", prop.Specifiers);

        var func = Assert.Single(foo.Functions);
        Assert.Equal("TakeDamage", func.Name);
        Assert.Equal("void", func.ReturnType);
        Assert.Equal("int32 Amount", func.Parameters);
    }

    [Fact]
    public void Parse_UsesFileNameFreeDeclaration_UstructWithNoModuleApiMacro() {
        WriteHeader("TestModule", "FBar.h",
            "USTRUCT(BlueprintType)\n" +
            "struct FBar {\n" +
            "\tUPROPERTY(EditAnywhere)\n" +
            "\tfloat Scale;\n" +
            "};\n");

        var types = UhtHeaderParser.Parse(_dir);

        var bar = types["FBar"];
        Assert.Equal("struct", bar.Kind);
        Assert.Null(bar.Parent);
        Assert.Equal("float", Assert.Single(bar.Properties).Type);
    }

    [Fact]
    public void Parse_FileWithNoReflectedType_IsSkippedLeniently() {
        WriteHeader("TestModule", "PlainHeader.h", "#pragma once\nclass NotReflected {};\n");

        var types = UhtHeaderParser.Parse(_dir);

        Assert.Empty(types);
    }

    [Fact]
    public void Parse_UnreadableFile_IsSkippedWithWarning_OtherFilesStillParse() {
        WriteHeader("TestModule", "Good.h", "USTRUCT()\nstruct FGood {\n};\n");
        WriteHeader("TestModule", "Locked.h", "USTRUCT()\nstruct FLocked {\n};\n");

        var lockedPath = Path.Combine(_dir, "TestModule", "Public", "Locked.h");
        var warnings = new List<string>();
        using (new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None)) {
            var types = UhtHeaderParser.Parse(_dir, warnings.Add);

            Assert.True(types.ContainsKey("FGood"));
            Assert.False(types.ContainsKey("FLocked"));
            Assert.Contains(warnings, w => w.Contains("Locked.h"));
        }
    }

    [Fact]
    public void Parse_DuplicateTypeName_KeepsFirstAndWarns() {
        WriteHeader("ModuleA", "Dup.h", "USTRUCT()\nstruct Dup {\n};\n");
        WriteHeader("ModuleB", "Dup.h", "USTRUCT()\nstruct Dup {\n};\n");

        var warnings = new List<string>();
        var types = UhtHeaderParser.Parse(_dir, warnings.Add);

        Assert.Single(types);
        Assert.Contains(warnings, w => w.Contains("duplicate UHT type name"));
    }

    // CODE-09: a trailing qualifier after the closing paren (const/override/etc) was previously discarded
    // entirely rather than captured, making a qualifier-only source change invisible to the diff.
    [Fact]
    public void Parse_FunctionWithTrailingQualifiers_CapturesThemSeparately() {
        WriteHeader("TestModule", "UFoo.h",
            "UCLASS()\n" +
            "class UFoo : public UObject {\n" +
            "\tUFUNCTION(BlueprintCallable)\n" +
            "\tvirtual bool CanDoThing() const override;\n" +
            "};\n");

        var types = UhtHeaderParser.Parse(_dir);

        var func = Assert.Single(types["UFoo"].Functions);
        Assert.Equal("CanDoThing", func.Name);
        Assert.Equal("", func.Parameters);
        Assert.Equal("const override", func.Qualifiers);
    }

    [Fact]
    public void Parse_FunctionWithNoTrailingQualifiers_LeavesQualifiersEmpty() {
        WriteHeader("TestModule", "UFoo.h",
            "UCLASS()\n" +
            "class UFoo : public UObject {\n" +
            "\tUFUNCTION(BlueprintCallable)\n" +
            "\tvoid DoThing();\n" +
            "};\n");

        var types = UhtHeaderParser.Parse(_dir);

        var func = Assert.Single(types["UFoo"].Functions);
        Assert.Equal("", func.Qualifiers);
    }
}
