using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace DekUnrealGameAudit.Core;

/// <summary>Options shared by both scan commands (hash-game-files / hash-mod-files).</summary>
public class ScanOptions {
    public required string TargetFolder { get; init; }
    public required string UeVersion { get; init; }
    public List<(string Guid, string Key)> AesKeys { get; init; } = new();
    public string? AesFile { get; init; }
    /// <summary>Caps how many assets are hashed concurrently in hash-game-files. Null (the default) leaves it to
    /// the runtime (effectively one per CPU core), which is fine on an SSD; capping it lower can help on a
    /// spinning disk where too many concurrent reads just thrash the head instead of finishing faster.</summary>
    public int? MaxDegreeOfParallelism { get; init; }
    /// <summary>CODE-16 scope rules (see ScopeClassifier) classifying containers under TargetFolder as
    /// Game/Mod/Ignore. Null (the default) is a deliberate compatibility guarantee: with no rule set,
    /// ProviderFactory mounts everything under TargetFolder exactly as it always has - this only changes
    /// behavior for a caller that explicitly opts in.</summary>
    public ScopeRuleSet? ScopeRules { get; init; }
}

public class AssetEntry {
    public string Hash { get; set; } = "";
    public long Size { get; set; }
}

public class AssetError {
    public string Path { get; set; } = "";
    public string Message { get; set; } = "";
}

public class GameManifest {
    public string GeneratedAt { get; set; } = "";
    public string UeVersion { get; set; } = "";
    public string PaksFolder { get; set; } = "";
    /// <summary>Best-effort ProjectVersion read from the game's own DefaultGame.ini, if it has one. Not every
    /// game populates this meaningfully - null when nothing usable was found.</summary>
    public string? DetectedGameVersion { get; set; }
    /// <summary>The version string actually used for filename suffixing - an explicit --version-tag override
    /// if one was given, otherwise the same as DetectedGameVersion.</summary>
    public string? VersionTag { get; set; }
    public Dictionary<string, AssetEntry> Assets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Assets that failed to read/hash (e.g. a parser quirk for this game/engine version) and were
    /// skipped rather than aborting the whole scan. Not included in Assets - GameCompareEngine cross-references
    /// this against the other manifest's Errors so a path that failed here isn't misreported as confidently
    /// Added/Removed just because it's absent from this manifest's Assets for an unrelated reason.</summary>
    public List<AssetError> Errors { get; set; } = new();
    /// <summary>ScopeClassifier.ComputeFingerprint of the ScanOptions.ScopeRules used to produce this manifest,
    /// or null when no scope rules were used (the pre-CODE-16 default). A future compare step can use this to
    /// detect two manifests using different (or unknown/legacy) scope, rather than treating a filter change as
    /// if it were a real game addition/removal - not yet wired into GameCompareEngine.</summary>
    public string? ScopeFingerprint { get; set; }
    /// <summary>Which AssetGrouper hashing scheme produced Assets' hashes - lets GameCompareEngine warn instead
    /// of silently showing every asset as Changed if one side predates a hash-scheme change (INV-04). A
    /// manifest saved before this field existed deserializes with this default (1 - the original, unframed
    /// scheme) since Newtonsoft only overwrites properties actually present in the incoming JSON; GameHasher
    /// always stamps newly-produced manifests with AssetGrouper.CurrentHashSchemeVersion.</summary>
    public int HashSchemeVersion { get; set; } = 1;
}

public class ModInfo {
    public List<string> Assets { get; set; } = new();
}

public class ModManifest {
    public string GeneratedAt { get; set; } = "";
    public string ModsFolder { get; set; } = "";
    public Dictionary<string, ModInfo> Mods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>See GameManifest.ScopeFingerprint - same meaning, null when no scope rules were used.</summary>
    public string? ScopeFingerprint { get; set; }
}

public class ChangedAsset {
    public string Path { get; set; } = "";
    public string OldHash { get; set; } = "";
    public string NewHash { get; set; } = "";
}

/// <summary>An asset whose Added/Removed/Changed status couldn't be determined, because one or both scans
/// failed to read it (see <see cref="GameManifest.Errors"/>) rather than confirming it present or absent.</summary>
public class UncertainAsset {
    public string Path { get; set; } = "";
    public string Reason { get; set; } = "";
}

public class GameCompareResult {
    public string GeneratedAt { get; set; } = "";
    /// <summary>Full resolved path of the manifest actually used as "old"/"new" for this comparison - set by
    /// the caller (CLI command or GUI view) right after loading it, not by <see cref="GameCompareEngine"/>
    /// itself, which stays decoupled from file I/O. Lets anyone opening this JSON/Markdown later answer
    /// "what was actually compared to produce this?" without having to trust whatever the GUI's input boxes
    /// happen to show now, which may have since changed.</summary>
    public string OldManifestPath { get; set; } = "";
    public string NewManifestPath { get; set; } = "";
    /// <summary>UE version each source manifest was hashed with - GameCompareEngine.Compare already warns via
    /// onLog when these differ, but didn't previously persist them anywhere for later inspection.</summary>
    public string OldUeVersion { get; set; } = "";
    public string NewUeVersion { get; set; } = "";
    public List<string> Added { get; set; } = new();
    public List<string> Removed { get; set; } = new();
    public List<ChangedAsset> Changed { get; set; } = new();
    public int UnchangedCount { get; set; }
    /// <summary>Assets that could not be confidently classified because a scan error stands in place of an
    /// Assets entry on one or both sides - see <see cref="UncertainAsset"/>. Never populated for a comparison
    /// between two error-free manifests.</summary>
    public List<UncertainAsset> Uncertain { get; set; } = new();
}

public class ModsCompareEntry {
    /// <summary>Assets this mod overrides that the game update changed or removed. A strong signal the mod
    /// is worth reviewing, not proof it's actually broken or must be republished - the field/JSON name is
    /// kept as-is for compatibility, but every rendered form of this (CLI, GUI, Markdown) presents it as
    /// "potentially affected - review required," not a certainty.</summary>
    public List<string> AffectedAssets { get; set; } = new();
    public bool NeedsUpdate { get; set; }
    /// <summary>This mod references an asset in the comparison's <see cref="GameCompareResult.Uncertain"/>
    /// list - the scan couldn't confirm whether it changed, so this mod isn't confidently unaffected even
    /// though it's not in <see cref="AffectedAssets"/>. Kept separate from NeedsUpdate/AffectedAssets so a
    /// confirmed change is never diluted by an unrelated scan gap, and vice versa.</summary>
    public List<string> UncertainAssets { get; set; } = new();
    public bool NeedsReview { get; set; }
    /// <summary>Assets this mod overrides that the game update newly *added* at that same path - the mod
    /// didn't override anything there before (there was nothing to override), but it does now, silently
    /// shadowing new official content (INV-03). A different kind of signal from <see cref="AffectedAssets"/>
    /// (a stale override of something that changed/vanished) - this is an unintended new collision with
    /// something that didn't exist when the mod was made. Folded into <see cref="NeedsReview"/> rather than a
    /// separate top-level bucket - see <see cref="ModsCompareEngine.Compare"/>.</summary>
    public List<string> CollidingAssets { get; set; } = new();
    public bool HasNewCollision { get; set; }
}

public class ModsCompareReport {
    public string GeneratedAt { get; set; } = "";
    /// <summary>Full resolved paths of the two inputs actually used - see
    /// <see cref="GameCompareResult.OldManifestPath"/> for why this is set by the caller, not the engine.</summary>
    public string GameComparePath { get; set; } = "";
    public string ModsManifestPath { get; set; } = "";
    public Dictionary<string, ModsCompareEntry> Mods { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class JsonUtil {
    private static readonly JsonSerializerSettings Settings = new() {
        Formatting = Formatting.Indented,
        // ProcessDictionaryKeys defaults to true, which would also camelCase Dictionary<string,T> keys (e.g.
        // asset paths, mod container names, header type names) on write - but ReadFile below never reverses
        // that on the way back in (deserialization just takes whatever key string is in the JSON verbatim),
        // so a written-then-read-back dictionary key could end up permanently mangled relative to the value
        // actually written (e.g. "FAIMimicGroupCooldown" -> "fAIMimicGroupCooldown"). Disabling it here keeps
        // dictionary keys exactly as written, matching what ReadFile already assumes.
        ContractResolver = new CamelCasePropertyNamesContractResolver {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false }
        }
    };

    /// <summary>Streams serialization straight into the atomic temp file (see <see cref="AtomicFile"/>) rather
    /// than building the whole JSON document as one string first - halves peak memory for a large manifest,
    /// and a failure partway through never touches the previous, good file at <paramref name="path"/>.</summary>
    public static void WriteFile(string path, object value) =>
        AtomicFile.WriteViaStream(path, stream => {
            using var writer = new StreamWriter(stream);
            using var jsonWriter = new JsonTextWriter(writer);
            JsonSerializer.Create(Settings).Serialize(jsonWriter, value);
        });

    /// <summary>Parses as a JObject first (rather than deserializing straight to T) so
    /// <see cref="DocumentShape"/> can check the document actually looks like a <typeparamref name="T"/> before
    /// trusting any of its contents - see <see cref="DocumentValidationException"/> for why that matters.</summary>
    public static T ReadFile<T>(string path) {
        JObject root;
        try {
            root = JObject.Parse(File.ReadAllText(path));
        } catch (JsonReaderException ex) {
            throw new DocumentValidationException($"'{path}' is not valid JSON: {ex.Message}");
        }

        DocumentShape.Validate<T>(root, path);

        return root.ToObject<T>(JsonSerializer.Create(Settings))
            ?? throw new InvalidDataException($"Failed to parse JSON file: {path}");
    }
}
