using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-08's log-flooding slice (see IMPROVEMENT-PLAN.md): a comparison
/// with tens of thousands of changes must not turn into tens of thousands of individual GUI log lines.</summary>
public class LogCappingTests {
    [Fact]
    public void Cap_YieldsEveryItemWhenUnderTheLimit() {
        var items = new List<string> { "a", "b", "c" };

        var result = LogCapping.Cap(items, s => s, max: 5).ToList();

        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void Cap_YieldsExactlyTheLimitWhenCountEqualsMax() {
        var items = new List<string> { "a", "b", "c" };

        var result = LogCapping.Cap(items, s => s, max: 3).ToList();

        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void Cap_TruncatesAndAppendsASummaryLineWhenOverTheLimit() {
        var items = Enumerable.Range(1, 10).Select(i => i.ToString()).ToList();

        var result = LogCapping.Cap(items, s => s, max: 4).ToList();

        Assert.Equal(5, result.Count); // 4 items + 1 summary line
        Assert.Equal(["1", "2", "3", "4"], result.Take(4));
        Assert.Contains("6 more", result[4]);
    }

    [Fact]
    public void Cap_UsesTheFormatFunctionForEachYieldedItem() {
        var items = new List<int> { 1, 2, 3 };

        var result = LogCapping.Cap(items, i => $"n={i}", max: 10).ToList();

        Assert.Equal(["n=1", "n=2", "n=3"], result);
    }

    [Fact]
    public void Cap_OnEmptyCollectionYieldsNothing() {
        var result = LogCapping.Cap(new List<string>(), s => s).ToList();

        Assert.Empty(result);
    }
}
