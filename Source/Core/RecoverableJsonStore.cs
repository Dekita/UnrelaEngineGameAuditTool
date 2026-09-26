using Newtonsoft.Json;

namespace DekUnrealGameAudit.Core;

/// <summary>Shared load/save behavior for small, single-file JSON stores (GUI settings, profiles) that
/// currently swallow every failure and silently fall back to an empty/default state - which loses a corrupt
/// file's contents forever the moment anything saves over it, and lets a failed "Save" look successful in
/// the UI. Pulled out of the GUI project so this behavior can be exercised by real tests instead of only ever
/// being verified by hand against the app's own real, non-redirectable app-data path.</summary>
public static class RecoverableJsonStore {
    /// <summary>Reads and deserializes JSON from <paramref name="path"/>. Returns default(T) if the file
    /// doesn't exist yet (nothing to recover - a normal first run). If it exists but fails to parse, the
    /// corrupt file is copied aside (never overwritten or deleted) before returning default(T), so a corrupt
    /// settings/profiles file is never silently discarded - the next successful save would otherwise replace
    /// it with fresh data with no way back.</summary>
    public static T? Load<T>(string path, Action<string>? onCorrupt = null) {
        if (!File.Exists(path))
            return default;

        try {
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
        } catch (Exception ex) {
            var backupPath = BackupCorruptFile(path);
            onCorrupt?.Invoke(backupPath != null
                ? $"'{path}' was corrupt and could not be loaded ({ex.Message}) - the original has been kept at '{backupPath}'."
                : $"'{path}' was corrupt and could not be loaded ({ex.Message}).");
            return default;
        }
    }

    /// <summary>Atomically writes <paramref name="value"/> as JSON to <paramref name="path"/>, returning
    /// whether it actually succeeded - never throws, so a caller that only wants best-effort auto-save
    /// behavior can freely ignore the result, while one representing an explicit user action (e.g. "Save
    /// profile") can report a real failure instead of looking like it worked.</summary>
    public static bool TrySave(string path, object value) {
        try {
            AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>Copies the corrupt file to "<paramref name="path"/>.corrupt-yyyyMMddHHmmss.bak" next to it,
    /// without touching or removing the original. Returns the backup path, or null if even the backup
    /// couldn't be made (e.g. the directory itself is unwritable) - in that case the original file is at
    /// least left in place untouched.</summary>
    private static string? BackupCorruptFile(string path) {
        try {
            var backupPath = $"{path}.corrupt-{DateTime.Now:yyyyMMddHHmmss}.bak";
            File.Copy(path, backupPath, overwrite: true);
            return backupPath;
        } catch {
            return null;
        }
    }
}
