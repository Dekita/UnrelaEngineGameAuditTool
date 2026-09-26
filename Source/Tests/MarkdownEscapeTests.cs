using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-10's escaping slice (see IMPROVEMENT-PLAN.md): a pipe, backtick or
/// newline embedded in an asset path, type name or error message must never corrupt the Markdown table or
/// code span it's rendered into.</summary>
public class MarkdownEscapeTests {
    [Fact]
    public void Cell_LeavesPlainTextUnchanged() {
        Assert.Equal("Content/Weapon.uasset", MarkdownEscape.Cell("Content/Weapon.uasset"));
    }

    [Fact]
    public void Cell_EscapesPipesSoTheyDoNotBreakTheTable() {
        Assert.Equal("a \\| b", MarkdownEscape.Cell("a | b"));
    }

    [Fact]
    public void Cell_ReplacesBackticksSoTheyDoNotBreakACodeSpan() {
        Assert.Equal("it's", MarkdownEscape.Cell("it`s"));
    }

    [Theory]
    [InlineData("a\nb", "a b")]
    [InlineData("a\r\nb", "a b")]
    [InlineData("a\rb", "a b")]
    public void Cell_CollapsesNewlinesSoTheyDoNotBreakTheRow(string input, string expected) {
        Assert.Equal(expected, MarkdownEscape.Cell(input));
    }

    [Fact]
    public void Cell_OfNullOrEmptyReturnsEmptyString() {
        Assert.Equal("", MarkdownEscape.Cell(null));
        Assert.Equal("", MarkdownEscape.Cell(""));
    }
}
