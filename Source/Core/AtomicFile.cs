namespace DekUnrealGameAudit.Core;

/// <summary>Writes a file atomically: a reader never observes a partially-written file, and a write that fails
/// partway through never touches the previous, good contents of the destination. Every manifest, comparison
/// report, Markdown export and settings/profile file this tool writes goes through here instead of
/// File.WriteAllText directly - writing straight to the destination leaves it truncated if the process dies
/// mid-write (disk full, crash, killed process, cancellation), silently destroying the last good file. Writing
/// to a fresh, uniquely-named temp file in the *same* directory and then swapping it in with
/// File.Replace/File.Move is all-or-nothing on Windows, and staying on the same directory keeps the swap on one
/// volume (a cross-volume move/replace is not atomic).</summary>
public static class AtomicFile {
    /// <summary>Writes <paramref name="content"/> as the complete contents of <paramref name="path"/>.</summary>
    public static void WriteAllText(string path, string content) =>
        WithTempFile(path, tempPath => File.WriteAllText(tempPath, content));

    /// <summary>Lower-level form for a caller that wants to stream directly into the temp file - e.g. a JSON
    /// serializer writing a large object graph without first materializing the whole document as one string.</summary>
    public static void WriteViaStream(string path, Action<Stream> writeContent) =>
        WithTempFile(path, tempPath => {
            using var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write);
            writeContent(stream);
        });

    private static void WithTempFile(string path, Action<string> writeTemp) {
        var fullPath = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(dir);

        // Guid-suffixed so concurrent writers (or a leftover temp file from a killed process) never collide.
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try {
            writeTemp(tempPath);

            // File.Replace requires an existing destination; a brand-new file just moves into place. Both are
            // atomic renames on the same volume - there is no window where a reader sees a partial file.
            if (File.Exists(fullPath))
                File.Replace(tempPath, fullPath, null);
            else
                File.Move(tempPath, fullPath);
        } finally {
            // Only ever touches the temp file this call created - never the caller's real destination.
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
