using System.IO;
using DekUnrealGameAudit.Core;
using Newtonsoft.Json;

namespace DekUnrealGameAudit.Gui;

/// <summary>Persisted window size, kept deliberately separate from <see cref="AppSettings"/>/<see cref="ProfileStore"/>
/// - window size is a preference tied to the current display, not to which game profile is active, and
/// AppSettings round-trips as a whole object into every saved profile snapshot, which would otherwise resize
/// the window on every profile switch. Falls back to MainWindow's own XAML Width/Height default (itself equal
/// to MinWidth/MinHeight) when nothing has been saved yet or the file is missing/corrupt.</summary>
public class WindowGeometry {
    public double? Width { get; set; }
    public double? Height { get; set; }

    private static string StorePath => Path.Combine(AppSettings.AppDataDir, "window-geometry.json");

    public static WindowGeometry Load() {
        try {
            if (File.Exists(StorePath))
                return JsonConvert.DeserializeObject<WindowGeometry>(File.ReadAllText(StorePath)) ?? new WindowGeometry();
        } catch {
            // Corrupt or unreadable - just use the XAML default rather than block startup on it.
        }
        return new WindowGeometry();
    }

    public void Save() {
        try {
            AtomicFile.WriteAllText(StorePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        } catch {
            // Best-effort - not worth failing over a window-size write.
        }
    }
}
