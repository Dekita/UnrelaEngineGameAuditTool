using DekUnrealGameAudit.Commands;

namespace DekUnrealGameAudit.Tests;

public class ArgMapTests {
    [Fact]
    public void Parse_SupportsSpaceAndEqualsAndRepeatedFlags() {
        var map = ArgMap.Parse(["--paks", "C:\\Game", "--ue-version=GAME_UE5_3", "--aes", "AAA", "--aes", "BBB", "-o", "out.json"]);

        Assert.Equal("C:\\Game", map.RequireOne("paks"));
        Assert.Equal("GAME_UE5_3", map.RequireOne("ue-version"));
        Assert.Equal(["AAA", "BBB"], map.Many("aes"));
        Assert.Equal("out.json", map.RequireOne("out"));
    }

    [Fact]
    public void RequireOne_ThrowsWithFlagNameWhenMissing() {
        var map = ArgMap.Parse([]);
        var ex = Assert.Throws<ArgumentException>(() => map.RequireOne("paks"));
        Assert.Contains("--paks", ex.Message);
    }

    [Fact]
    public void Parse_ThrowsOnStrayPositionalArgument() {
        var ex = Assert.Throws<ArgumentException>(() => ArgMap.Parse(["not-a-flag"]));
        Assert.Contains("not-a-flag", ex.Message);
    }

    [Fact]
    public void OneOrDefault_ThrowsWhenFlagIsRepeated() {
        var map = ArgMap.Parse(["--out", "a.json", "--out", "b.json"]);
        var ex = Assert.Throws<ArgumentException>(() => map.RequireOne("out"));
        Assert.Contains("--out", ex.Message);
    }

    [Fact]
    public void OneOrDefault_TreatsBlankValueAsAbsent() {
        var map = ArgMap.Parse(["--out", "  "]);
        Assert.Null(map.OneOrDefault("out"));
    }

    [Fact]
    public void EnsureKnownKeys_ThrowsNamingTheUnknownFlag() {
        var map = ArgMap.Parse(["--paks", "C:\\Game", "--pak", "typo"]);
        var ex = Assert.Throws<ArgumentException>(() => map.EnsureKnownKeys("paks", "ue-version"));
        Assert.Contains("--pak", ex.Message);
    }

    [Fact]
    public void EnsureKnownKeys_AllowsOnlyRecognizedFlags() {
        var map = ArgMap.Parse(["--paks", "C:\\Game", "--ue-version", "GAME_UE5_3"]);
        map.EnsureKnownKeys("paks", "ue-version"); // does not throw
    }

}

/// <summary>Regression coverage for CODE-13 (see IMPROVEMENT-PLAN.md): --max-parallelism must be validated
/// upfront as a positive integer, rather than reaching ParallelOptions and throwing a confusing
/// ArgumentOutOfRangeException only after a potentially long mount/scan is already underway.</summary>
public class HashGameFilesCommandParallelismTests {
    [Theory]
    [InlineData(null, null)]
    [InlineData("1", 1)]
    [InlineData("8", 8)]
    public void ParsePositiveParallelism_AcceptsNullOrPositiveIntegers(string? raw, int? expected) =>
        Assert.Equal(expected, HashGameFilesCommand.ParsePositiveParallelism(raw));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("-8")]
    [InlineData("not-a-number")]
    [InlineData("4.5")]
    public void ParsePositiveParallelism_RejectsNonPositiveOrNonIntegerValues(string raw) {
        var ex = Assert.Throws<ArgumentException>(() => HashGameFilesCommand.ParsePositiveParallelism(raw));
        Assert.Contains("max-parallelism", ex.Message);
    }
}
