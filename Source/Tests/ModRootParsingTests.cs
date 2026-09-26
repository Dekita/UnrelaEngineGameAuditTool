using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

public class ModRootParsingTests {
    [Fact]
    public void ParseModRoots_SingleLine_ResolvesToOneFullPath() {
        var roots = ModRootParsing.ParseModRoots("mods");

        var root = Assert.Single(roots);
        Assert.Equal(Path.GetFullPath("mods"), root);
    }

    [Fact]
    public void ParseModRoots_BlankOrNull_ReturnsEmpty() {
        Assert.Empty(ModRootParsing.ParseModRoots(null));
        Assert.Empty(ModRootParsing.ParseModRoots(""));
        Assert.Empty(ModRootParsing.ParseModRoots("   \n  \n "));
    }

    [Fact]
    public void ParseModRoots_MultipleLines_TrimsAndKeepsOrder() {
        var roots = ModRootParsing.ParseModRoots("  mods-a  \n\nmods-b\n");

        Assert.Equal([Path.GetFullPath("mods-a"), Path.GetFullPath("mods-b")], roots);
    }

    [Fact]
    public void ParseModRoots_ExactDuplicate_CollapsesToOneEntry() {
        var roots = ModRootParsing.ParseModRoots($"mods-a\nmods-b\n{Path.GetFullPath("mods-a")}");

        Assert.Equal([Path.GetFullPath("mods-a"), Path.GetFullPath("mods-b")], roots);
    }

    [Fact]
    public void ParseModRoots_DuplicateDifferingOnlyByCase_CollapsesToOneEntry() {
        var roots = ModRootParsing.ParseModRoots("Mods-A\nMODS-A");

        var root = Assert.Single(roots);
        Assert.Equal(Path.GetFullPath("Mods-A"), root);
    }
}
