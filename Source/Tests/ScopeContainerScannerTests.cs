using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-16's container discovery: finds .pak files and .utoc+.ucas pairs
/// under a scan root and classifies each one, diagnosing (not silently mounting) an incomplete IoStore pair.
/// Uses empty fixture files - discovery only inspects file names/extensions, never content.</summary>
public class ScopeContainerScannerTests : IDisposable {
    private readonly string _dir = Directory.CreateTempSubdirectory("scope-scanner-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void Touch(string relativePath) {
        var fullPath = Path.Combine(_dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "");
    }

    [Fact]
    public void DiscoverAndClassify_FindsRootAndNestedPaks_ClassifiedByDefaultHeuristic() {
        Touch("base.pak");
        Touch("~mods/a.pak");

        var (containers, diagnostics) = ScopeContainerScanner.DiscoverAndClassify(_dir, new ScopeRuleSet());

        Assert.Empty(diagnostics);
        Assert.Equal(2, containers.Count);
        Assert.Contains(containers, c => c.RootRelativePath == "base.pak" && c.Classification == ScopeAction.Game);
        Assert.Contains(containers, c => c.RootRelativePath == "~mods/a.pak" && c.Classification == ScopeAction.Mod);
    }

    [Fact]
    public void DiscoverAndClassify_CompleteUtocUcasPair_IsIncludedAsOneContainer() {
        Touch("OfficialDLC/dlc.utoc");
        Touch("OfficialDLC/dlc.ucas");

        var (containers, diagnostics) = ScopeContainerScanner.DiscoverAndClassify(_dir, new ScopeRuleSet());

        Assert.Empty(diagnostics);
        var container = Assert.Single(containers);
        Assert.Equal("OfficialDLC/dlc.utoc", container.RootRelativePath);
        Assert.Equal(ScopeAction.Mod, container.Classification); // nested folder, default heuristic
    }

    [Fact]
    public void DiscoverAndClassify_UtocWithoutMatchingUcas_IsSkippedAndDiagnosed() {
        Touch("broken/dlc.utoc");
        // no matching .ucas

        var (containers, diagnostics) = ScopeContainerScanner.DiscoverAndClassify(_dir, new ScopeRuleSet());

        Assert.Empty(containers);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("dlc.utoc", diagnostic.Path);
        Assert.Contains(".ucas", diagnostic.Message);
    }

    [Fact]
    public void Discover_NonContainerFiles_ProduceOneSummaryDiagnosticNotPerFileSpam() {
        Touch("base.pak");
        Touch("~mods/MyMod/MyMod.dll");
        Touch("~mods/MyMod/script.lua");
        Touch("~mods/MyMod/script2.lua");

        var (containers, diagnostics) = ScopeContainerScanner.Discover(_dir);

        Assert.Single(containers); // only base.pak - the .dll/.lua files aren't containers
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("3 unsupported file(s)", diagnostic.Message);
        Assert.Contains(".dll", diagnostic.Message);
        Assert.Contains(".lua", diagnostic.Message);
    }

    [Fact]
    public void Discover_UnclassifiedMode_TreatsEveryContainerAsAModRegardlessOfDepth() {
        // The classic dedicated-mods-folder workflow (no scope rules): every container found is a mod, root
        // or nested alike - unlike DiscoverAndClassify's Game-by-default-at-root heuristic.
        Touch("root-mod.pak");
        Touch("Nested/other-mod.pak");

        var (containers, diagnostics) = ScopeContainerScanner.Discover(_dir);

        Assert.Empty(diagnostics);
        Assert.Equal(2, containers.Count);
        Assert.Contains(containers, c => c.RootRelativePath == "root-mod.pak");
        Assert.Contains(containers, c => c.RootRelativePath == "Nested/other-mod.pak");
    }

    [Fact]
    public void DiscoverAndClassify_AppliesExplicitRulesNotJustTheDefault() {
        Touch("root-mod.pak");
        Touch("OfficialDLC/dlc.pak");

        var ruleSet = new ScopeRuleSet {
            Rules = [
                new ScopeRule { Path = "root-mod.pak", IsFolder = false, Action = ScopeAction.Mod },
                new ScopeRule { Path = "OfficialDLC", IsFolder = true, Action = ScopeAction.Game },
            ]
        };

        var (containers, _) = ScopeContainerScanner.DiscoverAndClassify(_dir, ruleSet);

        Assert.Contains(containers, c => c.RootRelativePath == "root-mod.pak" && c.Classification == ScopeAction.Mod);
        Assert.Contains(containers, c => c.RootRelativePath == "OfficialDLC/dlc.pak" && c.Classification == ScopeAction.Game);
    }
}
