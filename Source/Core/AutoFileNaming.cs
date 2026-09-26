namespace DekUnrealGameAudit.Core;

/// <summary>Shared by every BuildAutoFileName() across the three hashers. Date and detected version alone
/// aren't enough to guarantee two auto-named files never collide - running the same hash command twice in one
/// day (e.g. right before and right after an update, or just re-running to test something) can easily produce
/// the same version+date pair, silently overwriting whichever one you needed to keep. A short random suffix
/// makes every auto-named file unique regardless of when or how often you run it.</summary>
public static class AutoFileNaming {
    public static string RandomSuffix() => Guid.NewGuid().ToString("N")[..6];

    /// <summary>Calls <paramref name="buildCandidate"/> (which should pick a fresh <see cref="RandomSuffix"/>
    /// each time it runs, as every BuildAutoFileName() does) until it returns a path that doesn't already exist,
    /// so an auto-generated name is guaranteed never to silently overwrite an existing file - a 6-hex-character
    /// random suffix makes a collision unlikely, not impossible, and this is the difference between "probably
    /// unique" and "never replaces a previous scan".</summary>
    public static string AllocateUniquePath(Func<string> buildCandidate) {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++) {
            var candidate = buildCandidate();
            if (!File.Exists(candidate))
                return candidate;
        }
        throw new IOException($"Could not allocate a unique auto-generated filename after {maxAttempts} attempts.");
    }

    /// <summary>Directory-aware sibling of <see cref="AllocateUniquePath"/> - a run directory (CODE-18) is a
    /// folder, not a file, so <c>File.Exists</c> would never detect a collision. Creates the directory before
    /// returning, so the caller never has to check for a race between "path chosen" and "directory made".</summary>
    public static string AllocateUniqueDirectory(Func<string> buildCandidate) {
        const int maxAttempts = 20;
        for (var attempt = 0; attempt < maxAttempts; attempt++) {
            var candidate = buildCandidate();
            if (Directory.Exists(candidate))
                continue;
            Directory.CreateDirectory(candidate);
            return candidate;
        }
        throw new IOException($"Could not allocate a unique auto-generated directory after {maxAttempts} attempts.");
    }
}
