using System.Reflection;
using CUE4Parse.FileProvider;
using DekUnrealGameAudit.Core;
using UE4Config.Parsing;

namespace DekUnrealGameAudit.Tests;

public class GameVersionDetectorTests {
    [Fact]
    public void TryDetect_NullDefaultGame_ReturnsNullWithoutThrowing() {
        Assert.Null(GameVersionDetector.TryDetect((CustomConfigIni?)null));
    }

    [Fact]
    public void TryDetect_EmptyIniWithNoProjectVersion_ReturnsNull() {
        var ini = new CustomConfigIni("DefaultGame.ini");
        Assert.Null(GameVersionDetector.TryDetect(ini));
    }

    // INV-05's own decision criterion: "inject config-read failure and consider warning/continuing... without
    // suppressing cancellation or fatal failures." Reflectively nulls ConfigIni's internal Sections list - a
    // real, verified way to make the real CUE4Parse/UE4Config FindPropertyInstructions throw a genuine
    // NullReferenceException (confirmed live before writing this test), not a synthetic stand-in exception.
    [Fact]
    public void TryDetect_ConfigReadThrows_ReturnsNullAndLogsInsteadOfPropagating() {
        var ini = new CustomConfigIni("DefaultGame.ini");
        var sectionsField = typeof(ConfigIni).GetField("Sections", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        sectionsField.SetValue(ini, null);

        var logs = new List<string>();
        var result = GameVersionDetector.TryDetect(ini, onLog: logs.Add);

        Assert.Null(result);
        Assert.Contains(logs, l => l.Contains("could not detect game version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sanitize_ReplacesInvalidFileNameCharsWithDash() {
        var result = GameVersionDetector.Sanitize("1.3.0/Build:42");

        Assert.DoesNotContain('/', result);
        Assert.DoesNotContain(':', result);
        Assert.StartsWith("1.3.0", result);
    }

    [Fact]
    public void Sanitize_TrimsSurroundingWhitespace() {
        Assert.Equal("1.3.0", GameVersionDetector.Sanitize("  1.3.0  "));
    }

    [Fact]
    public void Sanitize_LeavesAlreadyValidStringsUnchanged() {
        Assert.Equal("1.3.0-hotfix", GameVersionDetector.Sanitize("1.3.0-hotfix"));
    }
}
