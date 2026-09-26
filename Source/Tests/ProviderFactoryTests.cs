using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-12 (see IMPROVEMENT-PLAN.md): options must be validated - and rejected -
/// before any CUE4Parse/Oodle initialization happens. These are the only two checks in ProviderFactory.Create that
/// run before touching CUE4Parse, so they're the only ones exercisable without a real paks folder.</summary>
public class ProviderFactoryTests {
    [Fact]
    public void Create_WithMissingFolder_ThrowsBeforeTouchingCue4Parse() {
        var options = new ScanOptions {
            TargetFolder = Path.Combine(Path.GetTempPath(), "provider-factory-test-does-not-exist-" + Guid.NewGuid()),
            UeVersion = "GAME_UE5_3",
        };

        var ex = Assert.Throws<DirectoryNotFoundException>(() => ProviderFactory.Create(options));
        Assert.Contains(options.TargetFolder, ex.Message);
    }

    [Fact]
    public void Create_WithUnknownUeVersion_ThrowsBeforeTouchingCue4Parse() {
        var dir = Directory.CreateTempSubdirectory("provider-factory-test-");
        try {
            var options = new ScanOptions { TargetFolder = dir.FullName, UeVersion = "NOT_A_REAL_UE_VERSION" };

            var ex = Assert.Throws<ArgumentException>(() => ProviderFactory.Create(options));
            Assert.Contains("NOT_A_REAL_UE_VERSION", ex.Message);
        } finally {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Create_WithNumericStringNotMatchingAnyEGameMember_ThrowsBeforeTouchingCue4Parse() {
        // Regression for CODE-13: Enum.TryParse alone accepts any string that parses as the enum's underlying
        // integer, even when no member actually has that value - this must still be rejected as unknown.
        var dir = Directory.CreateTempSubdirectory("provider-factory-test-");
        try {
            var options = new ScanOptions { TargetFolder = dir.FullName, UeVersion = "999999" };

            var ex = Assert.Throws<ArgumentException>(() => ProviderFactory.Create(options));
            Assert.Contains("999999", ex.Message);
        } finally {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Create_WithInvalidScopeRules_ThrowsBeforeTouchingCue4Parse() {
        // CODE-16: an invalid scope-rules file (e.g. two conflicting rules for the same path) must be
        // rejected before any mounting, the same way a bad folder/UE version already is.
        var dir = Directory.CreateTempSubdirectory("provider-factory-test-");
        try {
            var options = new ScanOptions {
                TargetFolder = dir.FullName,
                UeVersion = "GAME_UE5_3",
                ScopeRules = new ScopeRuleSet {
                    Rules = [
                        new ScopeRule { Path = "Mods", IsFolder = true, Action = ScopeAction.Mod },
                        new ScopeRule { Path = "Mods", IsFolder = true, Action = ScopeAction.Ignore },
                    ]
                },
            };

            Assert.Throws<ArgumentException>(() => ProviderFactory.Create(options));
        } finally {
            dir.Delete(recursive: true);
        }
    }
}
