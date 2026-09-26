using System.IO;
using System.Security.Cryptography;
using System.Text;
using DekUnrealGameAudit.Core;
using Newtonsoft.Json;

namespace DekUnrealGameAudit.Gui;

/// <summary>Field values for all six tabs, shared as a single instance across all of them (so switching
/// tabs never loses an unsaved edit) - auto-loaded on startup and auto-saved on close. Named, switchable
/// profiles built from the same fields are handled separately by <see cref="ProfileStore"/>. This is what
/// replaces hand-editing Scripts\settings.bat for GUI users.
///
/// Property names here persist to gui-settings.json/profiles.json using the exact C# name (no camelCase
/// resolver, unlike Core's JsonUtil). A property renamed since an earlier version keeps its original JSON
/// name via an explicit [JsonProperty] so existing saved settings/profiles keep loading correctly - same
/// technique <see cref="AesKeyForStorage"/> already uses for the encrypted AES key.</summary>
public class AppSettings {
    public event EventHandler? AesKeyOmittedOnSave;
    // Shared by Hash Game Files / Hash Mod Files
    public string? UeVersion { get; set; }

    /// <summary>Contains a real decryption key if the game is encrypted. Kept in plaintext in memory for
    /// everything else in the app to use directly, but persisted encrypted - see <see cref="AesKeyForStorage"/>.</summary>
    [JsonIgnore]
    public string? AesKey { get; set; }

    /// <summary>Set after a <see cref="Save"/> that had to omit <see cref="AesKey"/> because DPAPI protection
    /// failed - fails closed (see <see cref="AesKeyForStorage"/>) rather than ever writing the key as plain
    /// text, but that means the key silently isn't on disk until this is surfaced and the user re-saves. Not
    /// itself a UI notification - a view can check this after calling <see cref="Save"/> and warn the user.</summary>
    [JsonIgnore]
    public bool LastSaveOmittedAesKey { get; private set; }

    /// <summary>The on-disk form of <see cref="AesKey"/>: DPAPI-protected for the current Windows user/machine,
    /// so a copied gui-settings.json or profiles.json can't be decrypted anywhere else, unlike storing the key
    /// as plain text. The JSON property name ("AesKey") matches the plain-text field this replaces exactly
    /// (this class has no camelCase contract resolver, unlike Core's JsonUtil), so old saved settings/profiles
    /// still load - DpapiProtect.Unprotect treats a value that isn't a DPAPI blob (one written before this
    /// existed) as legacy plaintext and returns it as-is.
    ///
    /// Fails closed: if DPAPI protection fails, the key is omitted from the written file entirely (via
    /// <see cref="LastSaveOmittedAesKey"/>) rather than falling back to writing it as plain text - a broken
    /// user profile must not be able to turn "encrypt this key" into "store this key in the open".</summary>
    [JsonProperty("AesKey")]
    private string? AesKeyForStorage {
        get {
            var value = DpapiProtect.Protect(AesKey, out var succeeded);
            LastSaveOmittedAesKey = !succeeded;
            return succeeded ? value : null;
        }
        set => AesKey = DpapiProtect.Unprotect(value);
    }

    /// <summary>Optional path to a CODE-16 scope-rules JSON file (see Core.ScopeRuleSet), shared by Hash Game
    /// Files/Hash Mod Files the same way AesKey/UeVersion already are. Blank/null (the default) means no
    /// scope rules - fully unchanged mount-everything behavior, matching the CLI's own opt-in default.</summary>
    public string? ScopeRulesPath { get; set; }

    // Hash Game Files
    public string? PaksFolder { get; set; }
    public string? VersionTag { get; set; }
    public string? GameOutputPath { get; set; }

    // Hash Mod Files
    /// <summary>One-or-more mod root folders, one per line (CODE-17 multi-root scanning) - the same
    /// "multiple values, one per line" shape <see cref="AesKey"/> already uses. A single-line value behaves
    /// exactly as a single mod root always has; see <see cref="ModRootParsing"/>.</summary>
    public string? ModsFolder { get; set; }
    public string? ModsOutputPath { get; set; }

    // Compare Game Files
    public string? OldManifestPath { get; set; }
    public string? NewManifestPath { get; set; }
    [JsonProperty("DiffOutputPath")]
    public string? GameCompareOutputPath { get; set; }
    public string? GameCompareMdOutputPath { get; set; }

    // Compare Mod Files
    [JsonProperty("DiffPath")]
    public string? GameComparePath { get; set; }
    public string? ModsManifestPath { get; set; }
    [JsonProperty("CheckModsJsonOutputPath")]
    public string? ModsCompareJsonOutputPath { get; set; }
    [JsonProperty("CheckModsMdOutputPath")]
    public string? ModsCompareMdOutputPath { get; set; }

    // Hash Header Files
    public string? HeaderDumpFolder { get; set; }
    public string? HeaderOutputPath { get; set; }

    // Compare Header Files
    public string? OldHeaderManifestPath { get; set; }
    public string? NewHeaderManifestPath { get; set; }
    [JsonProperty("HeaderDiffOutputPath")]
    public string? HeaderCompareOutputPath { get; set; }
    [JsonProperty("HeaderDiffMdOutputPath")]
    public string? HeaderCompareMdOutputPath { get; set; }

    // Quick Actions - reuses PaksFolder/ModsFolder/UeVersion/AesKey/ScopeRulesPath/HeaderDumpFolder above
    // (blank HeaderDumpFolder means headers are disabled for Quick Actions, same "blank means off" convention
    // ScopeRulesPath already uses) - this is the only field Quick Actions owns on top of those.
    public string? QuickActionsOutputPath { get; set; }

    /// <summary>Per-user app data, not next to the exe - the exe's own folder is easy to zip up and share
    /// (e.g. the whole Output\ folder), which would leak a real decryption key stored in AesKey.</summary>
    public static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DekUnrealGameAudit");

    private static string DefaultPath => Path.Combine(AppDataDir, "gui-settings.json");

    /// <summary>One-time migration from the AppData folder used before this app was renamed from
    /// AssetsHashingTool to DekUnrealGameAudit - copies gui-settings.json/profiles.json over so real,
    /// already-saved settings/profiles aren't silently lost by the rename. Checked per file, not by whether
    /// the new folder as a whole already exists - an interrupted migration (app closed mid-copy) or a folder
    /// that already exists for an unrelated reason must not permanently block retrying whichever file is
    /// still missing. Never overwrites a file that already exists at the destination, migrated or not.</summary>
    private static void MigrateFromOldAppDataDir() {
        try {
            var oldDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AssetsHashingTool");
            if (!Directory.Exists(oldDir))
                return;

            Directory.CreateDirectory(AppDataDir);
            foreach (var fileName in new[] { "gui-settings.json", "profiles.json" }) {
                var newFile = Path.Combine(AppDataDir, fileName);
                if (File.Exists(newFile))
                    continue;
                var oldFile = Path.Combine(oldDir, fileName);
                if (File.Exists(oldFile))
                    File.Copy(oldFile, newFile);
            }
        } catch {
            // Best-effort - if migration fails for any reason, just start fresh rather than block the app on it.
        }
    }

    public static AppSettings Load() {
        MigrateFromOldAppDataDir();
        return RecoverableJsonStore.Load<AppSettings>(DefaultPath) ?? new AppSettings();
    }

    /// <summary>Auto-persist to the default per-user location. Best-effort/fire-and-forget by design (a
    /// failed background auto-save shouldn't interrupt an in-progress scan) - callers that represent an
    /// explicit user action instead check the returned success flag (see ProfileStore.SaveProfile).</summary>
    public bool Save() {
        var saved = RecoverableJsonStore.TrySave(DefaultPath, this);
        if (LastSaveOmittedAesKey)
            AesKeyOmittedOnSave?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    /// <summary>Populates THIS instance in place from another snapshot (e.g. a saved profile), rather than
    /// returning a new object - every view already holds a reference to the shared instance, so they all see
    /// the update once the caller refreshes their UI from it, without needing to be handed a new reference.</summary>
    public void CopyFrom(AppSettings other) {
        foreach (var property in ComparableProperties)
            property.SetValue(this, property.GetValue(other));
    }

    public AppSettings Snapshot() {
        var snapshot = new AppSettings();
        snapshot.CopyFrom(this);
        return snapshot;
    }

    public bool ContentEquals(AppSettings other) => ComparableProperties.All(property =>
        string.Equals((string?)property.GetValue(this), (string?)property.GetValue(other), StringComparison.Ordinal));

    // Every user-editable setting is a public string property. Deriving the comparison/copy list from that
    // contract means a future field automatically participates in profile dirty tracking without also being
    // added to a second hand-maintained list. Runtime status such as LastSaveOmittedAesKey is intentionally
    // excluded because it is not a user setting.
    private static readonly System.Reflection.PropertyInfo[] ComparableProperties = typeof(AppSettings)
        .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
        .Where(property => property.PropertyType == typeof(string) && property.CanRead && property.CanWrite)
        .ToArray();
}
