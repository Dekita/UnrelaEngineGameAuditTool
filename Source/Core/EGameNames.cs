using System.Text.RegularExpressions;
using CUE4Parse.UE4.Versions;

namespace DekUnrealGameAudit.Core;

/// <summary>Every valid <c>--ue-version</c> value, read straight from CUE4Parse's own EGame enum so the list
/// can never drift out of sync with whatever CUE4Parse version this is built against. Plain engine versions
/// (GAME_UE4_27, GAME_UE5_3, ...) are listed first in proper version order - what most users actually want -
/// followed by the much longer tail of per-game names, alphabetically.</summary>
public static class EGameNames {
    /// <summary>Matches GAME_UE&lt;major&gt;_&lt;anything&gt; - not just plain numbers, since CUE4Parse also
    /// has a few generic-engine variants shaped like GAME_UE4_25_Plus or GAME_UE5_EA.</summary>
    private static readonly Regex EngineVersionPattern = new(@"^GAME_UE(\d+)_(.+)$");

    public static readonly IReadOnlyList<string> All = BuildList();

    private static List<string> BuildList() {
        var names = Enum.GetNames<EGame>();

        var engineVersions = names.Where(n => EngineVersionPattern.IsMatch(n))
            .OrderBy(EngineVersionSortKey)
            .ToList();
        var gameSpecific = names.Where(n => !EngineVersionPattern.IsMatch(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

        engineVersions.AddRange(gameSpecific);
        return engineVersions;
    }

    private static (int Major, double MinorRank) EngineVersionSortKey(string name) {
        var match = EngineVersionPattern.Match(name);
        var major = int.Parse(match.Groups[1].Value);
        var suffix = match.Groups[2].Value;

        double minorRank = suffix switch {
            "LATEST" => double.MaxValue,
            "EA" => -0.5, // pre-release, sorts just before that major version's "_0"
            _ when suffix.EndsWith("_Plus") && int.TryParse(suffix[..^"_Plus".Length], out var plusBase) => plusBase + 0.5,
            _ when int.TryParse(suffix, out var minor) => minor,
            _ => double.MaxValue - 1, // unrecognized shape - keep it near the end of its major version rather than guess
        };

        return (major, minorRank);
    }
}
