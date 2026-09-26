using System.Security.Cryptography;
using System.Text;
using CUE4Parse.FileProvider.Objects;
using CUE4Parse.UE4.VirtualFileSystem;

namespace DekUnrealGameAudit.Core;

/// <summary>
/// Groups raw pak/IoStore entries into "logical assets" (a package's .uasset/.umap +
/// its .uexp/.ubulk/.uptnl payloads collapsed into one unit) and hashes their decompressed content.
/// </summary>
public static class AssetGrouper {
    /// <summary>Drops the mount-root segment (e.g. "GameName/Content/..." -> "Content/...") so paths
    /// line up across different game builds and between game/mod manifests with different mount roots -
    /// except "Engine", which is Unreal's own stable, project-independent content root and is never stripped
    /// (INV-02). Blindly stripping every root let Engine content and a game's own content collide into the
    /// same key whenever their sub-paths happened to match (e.g. two different assets both ending up
    /// "Content/Localization/..." after stripping) - confirmed real, not hypothetical, via a live mount probe
    /// against a real Unreal .pak where every entry was genuinely rooted at "Engine/...". Any other root is
    /// assumed to be the scanned project's own mount-root name, which can legitimately differ across a
    /// project's own builds/renames, so it's still stripped for cross-build comparability - unchanged from
    /// before this fix.</summary>
    public static string NormalizePath(string rawPath) {
        var trimmed = rawPath.Replace('\\', '/').TrimStart('/');
        var slash = trimmed.IndexOf('/');
        if (slash < 0)
            return trimmed;

        var root = trimmed[..slash];
        return string.Equals(root, "Engine", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed[(slash + 1)..];
    }

    public static string GetContainerName(GameFile file) =>
        file is VfsEntry vfsEntry ? vfsEntry.Vfs.Name : "loose-files";

    public static string GetLogicalKey(GameFile file) {
        var raw = file.IsUePackage || file.IsUePackagePayload ? file.PathWithoutExtension : file.Path;
        return NormalizePath(raw);
    }

    public static Dictionary<string, List<GameFile>> GroupByLogicalAsset(IEnumerable<GameFile> files) {
        var groups = new Dictionary<string, List<GameFile>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            var key = GetLogicalKey(file);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<GameFile>();
            list.Add(file);
        }
        return groups;
    }

    /// <summary>Orders logical assets by (container, on-disk offset) instead of dictionary order, so hashing
    /// walks each container's bytes roughly front-to-back instead of jumping around - better read locality
    /// for the OS/drive. Doesn't change what gets hashed, only the order, so results are unaffected.</summary>
    public static List<KeyValuePair<string, List<GameFile>>> OrderForSequentialRead(Dictionary<string, List<GameFile>> groups) =>
        groups
            .OrderBy(kvp => GetContainerName(kvp.Value[0]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(kvp => kvp.Value.Min(GetOffset))
            .ToList();

    private static long GetOffset(GameFile file) => file is VfsEntry vfsEntry ? vfsEntry.Offset : 0;

    /// <summary>Groups entries by their containing pak/utoc container, then by logical asset path.
    /// No content is read - used for hash-mod-files, which only needs to know which assets a mod touches.</summary>
    public static Dictionary<string, SortedSet<string>> GroupContainerAssets(IEnumerable<GameFile> files, Action<string>? onLog = null) {
        var result = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        // Tracks which underlying Vfs instance first claimed each container name, so two distinct mod
        // paks that happen to share a filename (common - many modders ship a generic "*_p.pak") get
        // flagged instead of silently merging into one mod entry with no way to tell them apart.
        var owners = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files) {
            var container = GetContainerName(file);
            if (!result.TryGetValue(container, out var set))
                result[container] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            set.Add(GetLogicalKey(file));

            if (file is VfsEntry vfsEntry) {
                if (!owners.TryGetValue(container, out var owner))
                    owners[container] = vfsEntry.Vfs;
                else if (!ReferenceEquals(owner, vfsEntry.Vfs) && warned.Add(container))
                    onLog?.Invoke($"  WARN: multiple pak files share the container name '{container}' - " +
                        "their assets are being merged into a single mod entry. Rename one of the paks " +
                        "to tell them apart in the report.");
            }
        }
        return result;
    }

    /// <summary>The current AssetGrouper hashing scheme - bumped whenever HashFramedParts' algorithm changes
    /// in a way that changes its output for the same input, so GameCompareEngine can warn instead of silently
    /// showing every asset as Changed when comparing manifests from before/after such a change. 1 was the
    /// original, unframed scheme (see HashFramedParts); 2 is the current, framed one (INV-04).</summary>
    public const int CurrentHashSchemeVersion = 2;

    /// <summary>Pure core of HashParts below, taking each part's own path (for sort order and the extension
    /// used in framing) and its already-read bytes - unlike HashParts itself, this needs no real GameFile/
    /// CUE4Parse provider, so it's fully unit-testable. Frames each part with its extension + byte length
    /// before its content, so two logically different splits of the same total bytes (e.g. content shifting
    /// from .uexp into .ubulk between game versions) can never hash identically just because their raw
    /// concatenated bytes happen to coincide - the original scheme concatenated bytes with no separator at
    /// all, which this replaces (INV-04, CurrentHashSchemeVersion 1 -> 2).</summary>
    public static (string Hash, long Size) HashFramedParts(IEnumerable<(string Path, byte[] Bytes)> parts) {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long size = 0;
        foreach (var (path, bytes) in parts.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase)) {
            var frame = Encoding.UTF8.GetBytes($"{System.IO.Path.GetExtension(path)}:{bytes.LongLength}\n");
            hasher.AppendData(frame);
            hasher.AppendData(bytes);
            size += bytes.LongLength;
        }
        return (Convert.ToHexString(hasher.GetHashAndReset()), size);
    }

    /// <summary>Hashes the decompressed/decrypted bytes of every physical part of a logical asset,
    /// in a fixed order, so the result is stable across runs regardless of pak compression/encryption.</summary>
    public static (string Hash, long Size) HashParts(List<GameFile> parts) =>
        HashFramedParts(parts.Select(p => (p.Path, p.Read())));
}
