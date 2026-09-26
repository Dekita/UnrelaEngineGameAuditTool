namespace DekUnrealGameAudit.Core;

/// <summary>A physical container found under a scan root, before any classification.</summary>
public record DiscoveredContainer(string AbsolutePath, string RootRelativePath);

/// <summary>Finds physical containers (.pak files, and .utoc+.ucas IoStore pairs) under a scan root - the
/// CODE-16 "apply scope before provider mounting" step: this runs before <see cref="ProviderFactory"/>
/// registers anything with CUE4Parse, so excluded/mod content is never handed to the provider in the first
/// place. Recurses one directory at a time rather than <c>Directory.EnumerateFiles(..., AllDirectories)</c>,
/// which both aborts the *entire* walk on the first inaccessible subdirectory and follows directory
/// junctions/symlinks with no way to opt out (CODE-17) - neither is acceptable for a real Paks tree, which can
/// have permission quirks and, in principle, a reparse point pointing anywhere, including back up into an
/// ancestor (a cycle) or outside the scan root entirely.</summary>
public static class ScopeContainerScanner {
    /// <summary>Discovers every container under <paramref name="rootFolder"/>, unclassified - used for the
    /// classic dedicated-mods-folder workflow (no scope rules), where every container found is a mod, the same
    /// as before CODE-16/17; only the mounting strategy around this changed.</summary>
    public static (List<DiscoveredContainer> Containers, List<ScopeDiagnostic> Diagnostics) Discover(
        string rootFolder, CancellationToken cancellationToken = default) {
        var pakFiles = new List<(string Absolute, string Relative)>();
        var utocFiles = new List<(string Absolute, string Relative)>();
        var otherExtensionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<ScopeDiagnostic>();

        Walk(rootFolder, rootFolder, pakFiles, utocFiles, otherExtensionCounts, diagnostics, cancellationToken);

        var containers = pakFiles.Select(f => new DiscoveredContainer(f.Absolute, f.Relative)).ToList();

        // A .utoc without its .ucas (or vice versa) is an incomplete IoStore container - CUE4Parse can't mount
        // it correctly, so this is diagnosed and skipped rather than handed to the provider as a partial
        // selection that would silently under-report coverage.
        foreach (var (absolute, relative) in utocFiles) {
            cancellationToken.ThrowIfCancellationRequested();
            var ucasPath = Path.ChangeExtension(absolute, ".ucas");
            if (!File.Exists(ucasPath)) {
                diagnostics.Add(new ScopeDiagnostic(relative, $"'{relative}' has no matching .ucas file - skipped (incomplete IoStore container)."));
                continue;
            }
            containers.Add(new DiscoveredContainer(absolute, relative));
        }

        // Distinguishes recursive discovery from format support: a mod folder visited during this walk may
        // also contain loose UE4SS DLL/Lua files or other non-container content - those are found, but not
        // pak/IoStore, so they're never asset-audited. One summary diagnostic rather than per-file spam.
        if (otherExtensionCounts.Count > 0) {
            var breakdown = string.Join(", ", otherExtensionCounts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {kv.Key}"));
            var total = otherExtensionCounts.Values.Sum();
            diagnostics.Add(new ScopeDiagnostic("", $"{total} unsupported file(s) found ({breakdown}) - not pak/IoStore content, so not included in this audit (e.g. UE4SS DLL/Lua mods need separate verification)."));
        }

        return (containers, diagnostics);
    }

    /// <summary>Discovers and classifies every container under <paramref name="rootFolder"/> via
    /// <see cref="ScopeClassifier"/> - used when scope rules are supplied.</summary>
    public static (List<ClassifiedContainer> Containers, List<ScopeDiagnostic> Diagnostics) DiscoverAndClassify(
        string rootFolder, ScopeRuleSet ruleSet, CancellationToken cancellationToken = default) {
        var (found, diagnostics) = Discover(rootFolder, cancellationToken);
        var containers = found
            .Select(f => {
                cancellationToken.ThrowIfCancellationRequested();
                return new ClassifiedContainer(f.AbsolutePath, f.RootRelativePath, ScopeClassifier.Classify(f.RootRelativePath, ruleSet));
            })
            .ToList();
        return (containers, diagnostics);
    }

    private static void Walk(
        string dir, string rootFolder,
        List<(string Absolute, string Relative)> pakFiles, List<(string Absolute, string Relative)> utocFiles,
        Dictionary<string, int> otherExtensionCounts, List<ScopeDiagnostic> diagnostics,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        List<string> files;
        List<string> subdirs;
        try {
            // Directory.EnumerateFiles/EnumerateDirectories are lazy - .ToList() forces the actual I/O (and
            // therefore any UnauthorizedAccessException/IOException) to happen inside this try, not later
            // during enumeration by the caller.
            files = Directory.EnumerateFiles(dir).ToList();
            subdirs = Directory.EnumerateDirectories(dir).ToList();
        } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            diagnostics.Add(new ScopeDiagnostic(RelativePath(rootFolder, dir), $"'{RelativePath(rootFolder, dir)}' is not accessible - skipped ({ex.Message})"));
            return;
        }

        foreach (var file in files) {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = RelativePath(rootFolder, file);
            var ext = Path.GetExtension(file);
            if (string.Equals(ext, ".pak", StringComparison.OrdinalIgnoreCase)) {
                pakFiles.Add((file, relative));
            } else if (string.Equals(ext, ".utoc", StringComparison.OrdinalIgnoreCase)) {
                utocFiles.Add((file, relative));
            } else if (!string.Equals(ext, ".ucas", StringComparison.OrdinalIgnoreCase)) {
                var key = ext.Length > 0 ? ext : "(no extension)";
                otherExtensionCounts[key] = otherExtensionCounts.GetValueOrDefault(key) + 1;
            }
        }

        foreach (var subdir in subdirs) {
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryInfo info;
            try {
                info = new DirectoryInfo(subdir);
                _ = info.Attributes; // forces the actual filesystem query now, inside this try
            } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
                diagnostics.Add(new ScopeDiagnostic(RelativePath(rootFolder, subdir), $"'{RelativePath(rootFolder, subdir)}' is not accessible - skipped ({ex.Message})"));
                continue;
            }

            // Never follow a directory junction/symlink - avoids both cycles (a reparse point pointing back
            // up into an ancestor) and silently escaping the scan root onto an unrelated part of the disk.
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                diagnostics.Add(new ScopeDiagnostic(RelativePath(rootFolder, subdir), $"'{RelativePath(rootFolder, subdir)}' is a symlink/junction - skipped (not followed)."));
                continue;
            }

            Walk(subdir, rootFolder, pakFiles, utocFiles, otherExtensionCounts, diagnostics, cancellationToken);
        }
    }

    private static string RelativePath(string rootFolder, string fullPath) =>
        Path.GetRelativePath(rootFolder, fullPath).Replace('\\', '/');
}
