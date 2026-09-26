namespace DekUnrealGameAudit.Core;

/// <summary>Orchestrates both header dump parsers against a UE4SS output folder (the folder containing
/// CXXHeaderDump/UHTHeaderDump as siblings, e.g. "&lt;Game&gt;\Binaries\Win64\ue4ss") into one manifest.
/// Plain local text parsing, not pak/CUE4Parse I/O, so unlike GameHasher this doesn't need parallelism -
/// a sequential pass over a few thousand small text files is already fast.</summary>
public static class HeaderHasher {
    public static HeaderManifest Hash(string ue4ssFolder, Action<string>? onLog = null,
        Action<int, int>? onProgress = null, CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(ue4ssFolder))
            throw new DirectoryNotFoundException($"Folder not found: {ue4ssFolder}");

        var manifest = new HeaderManifest {
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            SourceFolder = ue4ssFolder,
        };

        var cxxFolder = Path.Combine(ue4ssFolder, "CXXHeaderDump");
        var uhtFolder = Path.Combine(ue4ssFolder, "UHTHeaderDump");
        var foundEither = false;

        manifest.CxxDumpFound = Directory.Exists(cxxFolder);
        manifest.UhtDumpFound = Directory.Exists(uhtFolder);

        if (manifest.CxxDumpFound) {
            foundEither = true;
            var fileCount = Directory.EnumerateFiles(cxxFolder, "*.hpp", SearchOption.TopDirectoryOnly).Count();
            onLog?.Invoke($"Parsing {fileCount} CXXHeaderDump file(s)...");
            var (types, enums) = CxxHeaderParser.Parse(cxxFolder, onLog, cancellationToken);
            manifest.CxxTypes = types;
            manifest.CxxEnums = enums;
            onProgress?.Invoke(1, 2);
            onLog?.Invoke($"Found {types.Count} type(s), {enums.Count} enum(s) with layout data.");
        } else {
            onLog?.Invoke("No CXXHeaderDump folder found - skipping memory layout data.");
        }

        if (manifest.UhtDumpFound) {
            foundEither = true;
            onLog?.Invoke("Parsing UHTHeaderDump...");
            manifest.UhtTypes = UhtHeaderParser.Parse(uhtFolder, onLog, cancellationToken);
            onProgress?.Invoke(2, 2);
            onLog?.Invoke($"Found {manifest.UhtTypes.Count} reflected type(s) with source data.");
        } else {
            onLog?.Invoke("No UHTHeaderDump folder found - skipping source-level data.");
        }

        if (!foundEither)
            throw new InvalidOperationException(
                $"Neither CXXHeaderDump nor UHTHeaderDump was found under: {ue4ssFolder}");

        return manifest;
    }

    /// <summary>Auto-generated filename used when the caller doesn't specify one explicitly, mirroring
    /// GameHasher.BuildAutoFileName.</summary>
    public static string BuildAutoFileName() => $"header-manifest-{DateTime.UtcNow:yyyyMMdd}-{AutoFileNaming.RandomSuffix()}.json";
}
