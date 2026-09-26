namespace DekUnrealGameAudit.Core;

/// <summary>Neutralizes characters that would otherwise corrupt a Markdown table cell or the backtick code
/// spans this app's renderers wrap values in - not full CommonMark-spec escaping, just enough that a pipe,
/// backtick or newline embedded in an asset path, type name, error message or raw dump line can never break
/// the table it's rendered into or prematurely close a code span.</summary>
public static class MarkdownEscape {
    public static string Cell(string? text) {
        if (string.IsNullOrEmpty(text))
            return "";
        return text
            .Replace("`", "'")
            .Replace("|", "\\|")
            .Replace("\r\n", " ")
            .Replace("\n", " ")
            .Replace("\r", " ");
    }
}
