namespace DekUnrealGameAudit.Core;

/// <summary>Parses AES decryption key text into the (Guid, Key) shape <see cref="ScanOptions.AesKeys"/> expects
/// - moved out of <c>Commands.ArgMap</c> (INV-06): this has always been pure, CLI-independent parsing logic (no
/// dependency on ArgMap's own flag-map state), but living in the <c>Commands</c> namespace meant every GUI view
/// needing it had to reference CLI-specific code just to parse a key string. Home for shared parsing that isn't
/// actually about command-line flags.</summary>
public static class AesKeyParsing {
    /// <summary>Splits the GUI's multi-line AES key box (one "guid:hexkey" or bare "hexkey" entry per line)
    /// into the same shape <see cref="ParseAesKeys"/> expects, so the GUI needs no parsing logic of its own.</summary>
    public static List<(string Guid, string Key)> ParseAesKeysMultiline(string? text) =>
        ParseAesKeys((text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Parses `--aes` values of the form "guid:hexkey" or bare "hexkey" (implies the game's main key GUID).</summary>
    public static List<(string Guid, string Key)> ParseAesKeys(IEnumerable<string> rawValues) {
        var result = new List<(string, string)>();
        foreach (var raw in rawValues) {
            var parts = raw.Split(':', 2);
            result.Add(parts.Length == 2
                ? (parts[0], parts[1])
                : (ProviderFactory.MainKeyGuid, parts[0]));
        }
        return result;
    }
}
