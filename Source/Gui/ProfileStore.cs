using System.IO;
using DekUnrealGameAudit.Core;
using Newtonsoft.Json;

namespace DekUnrealGameAudit.Gui;

/// <summary>Named snapshots of <see cref="AppSettings"/>, all kept together in one app-data JSON file so the
/// user picks a profile by name from the sidebar instead of manually managing separate files on disk.</summary>
public class ProfileStore {
    private static string StorePath => Path.Combine(AppSettings.AppDataDir, "profiles.json");

    public Dictionary<string, AppSettings> Profiles { get; private set; } = new();
    public bool LastSaveOmittedAesKey { get; private set; }

    public static ProfileStore Load() {
        var store = new ProfileStore();
        store.Profiles = RecoverableJsonStore.Load<Dictionary<string, AppSettings>>(StorePath) ?? new Dictionary<string, AppSettings>();
        return store;
    }

    private bool Save() => RecoverableJsonStore.TrySave(StorePath, Profiles);

    /// <summary>Stores an independent snapshot of <paramref name="settings"/> - a round-trip through JSON
    /// rather than the live reference, so further edits to the shared settings instance don't silently drift
    /// an already-saved profile until the user explicitly saves over it again. Returns whether it actually
    /// persisted to disk - on failure, the in-memory entry is rolled back to what it was before this call, so
    /// the profile list can never claim a save succeeded when it didn't.</summary>
    public bool SaveProfile(string name, AppSettings settings) {
        var hadPrevious = Profiles.TryGetValue(name, out var previous);
        var snapshot = settings.Snapshot();
        Profiles[name] = snapshot;
        var saved = Save();
        LastSaveOmittedAesKey = snapshot.LastSaveOmittedAesKey;
        if (saved)
            return true;

        if (hadPrevious) Profiles[name] = previous!;
        else Profiles.Remove(name);
        return false;
    }

    /// <summary>Returns whether the deletion actually persisted to disk - on failure, the in-memory entry is
    /// restored so the profile list doesn't claim a deletion that didn't stick.</summary>
    public bool DeleteProfile(string name) {
        if (!Profiles.TryGetValue(name, out var existing))
            return true;

        Profiles.Remove(name);
        if (Save())
            return true;

        Profiles[name] = existing;
        return false;
    }
}
