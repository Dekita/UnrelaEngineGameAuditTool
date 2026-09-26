using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DekUnrealGameAudit.Core;

/// <summary>Options for a single Before/After capture run - the union of what GameHasher/ModsHasher/
/// HeaderHasher each need, plus where the run/baseline data itself lives.</summary>
public class QuickActionOptions {
    public required string PaksFolder { get; init; }
    public required string ModsFolder { get; init; }
    public required string UeVersion { get; init; }
    public List<(string Guid, string Key)> AesKeys { get; init; } = new();
    public string? AesFile { get; init; }
    public ScopeRuleSet? ScopeRules { get; init; }
    /// <summary>UE4SS dump folder for the optional header stage. Null means the header stage is Skipped, not
    /// attempted - "make headers a profile option" from CODE-18's own wording.</summary>
    public string? HeaderFolder { get; init; }
    /// <summary>Where run directories and the active-baseline pointer live - distinct from any single
    /// manifest's own output path, since a run produces a whole directory of artifacts.</summary>
    public required string OutputRoot { get; init; }
    /// <summary>Free-text label for display only (e.g. a GUI caller's profile name) - never used for lookup.</summary>
    public string? ProfileLabel { get; init; }
    public int? MaxDegreeOfParallelism { get; init; }
}

public enum StageStatus { Skipped, Completed, Failed, Partial }

/// <summary>Everything captured (or attempted) in one Before/After run - persisted as run-record.json inside
/// the run's own directory. Deliberately never includes AES keys/files, per CODE-18's explicit requirement.</summary>
public class RunRecord {
    public string RunId { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UeVersion { get; set; } = "";
    public string? ScopeFingerprint { get; set; }
    public string? ProfileLabel { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public StageStatus GameStage { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public StageStatus ModsStage { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public StageStatus HeaderStage { get; set; }

    public string? GameManifestPath { get; set; }
    public string? ModsManifestPath { get; set; }
    public string? HeaderManifestPath { get; set; }

    /// <summary>Short, human-readable notes on why a stage isn't Completed - a distilled subset of the full
    /// log stream, kept here so a RunRecord is self-explanatory even without the log.txt alongside it.</summary>
    public List<string> Notes { get; set; } = new();
}

/// <summary>Mod containers that appeared/disappeared since the baseline - see ModsCompareEngine.CompareInventory.
/// Distinct from mod IMPACT (ModsCompareReport): this is about the mod collection itself, not what a game
/// update did to it.</summary>
public class ModInventoryDelta {
    public List<string> Added { get; set; } = new();
    public List<string> Removed { get; set; } = new();
}

/// <summary>The combined result of an After run - a lightweight rollup with counts and paths to the full,
/// already-rendered sub-reports (game-comparison.md, mods-report.md, header-comparison.md) sitting alongside it
/// in the same run directory, rather than duplicating their detailed content here.</summary>
public class QuickActionReport {
    public string GeneratedAt { get; set; } = "";
    public string BeforeRunId { get; set; } = "";
    public string AfterRunId { get; set; } = "";

    public int GameAdded { get; set; }
    public int GameRemoved { get; set; }
    public int GameChanged { get; set; }
    public int GameUnchanged { get; set; }
    public int GameUncertain { get; set; }
    public string? GameComparePath { get; set; }

    public int ModsNeedingReview { get; set; }
    public int ModsNeedingUncertainReview { get; set; }
    public int ModsUpToDate { get; set; }
    public string? ModsComparePath { get; set; }

    public ModInventoryDelta ModInventoryDelta { get; set; } = new();

    public int? HeaderCxxChangedTypes { get; set; }
    public int? HeaderUhtChangedTypes { get; set; }
    public string? HeaderComparePath { get; set; }

    public List<string> Warnings { get; set; } = new();
    public List<string> SkippedOrFailedStages { get; set; } = new();
}
