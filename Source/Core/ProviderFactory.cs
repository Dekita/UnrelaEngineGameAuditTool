using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;

namespace DekUnrealGameAudit.Core;

public static class ProviderFactory {
    /// <summary>GUID CUE4Parse/FModel conventionally use for a game's single main AES key.</summary>
    public const string MainKeyGuid = "00000000000000000000000000000000";

    /// <summary>Mounts paks/IoStore containers for <paramref name="options"/>.TargetFolder. Three modes,
    /// checked in this order: (1) <paramref name="explicitContainerPaths"/> given (CODE-17) - registers exactly
    /// those container(s) and nothing else, skipping both <c>Initialize()</c> and scope-rules discovery
    /// entirely; used by ModsHasher to mount one container at a time so CUE4Parse's own one-winner-per-virtual-
    /// path merge can never hide a second mod's overlapping asset. (2) <paramref name="options"/>.ScopeRules set
    /// (CODE-16) - discovers/classifies containers under TargetFolder and registers only the ones matching
    /// <paramref name="wantedActions"/> (default: just Game). (3) Neither given - unchanged from before CODE-16:
    /// <c>Initialize()</c> mounts everything CUE4Parse itself finds under TargetFolder.</summary>
    public static DefaultFileProvider Create(
        ScanOptions options,
        ScopeAction[]? wantedActions = null,
        IReadOnlyList<string>? explicitContainerPaths = null,
        Action<string>? onLog = null,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(options.TargetFolder))
            throw new DirectoryNotFoundException($"Folder not found: {options.TargetFolder}");

        // Enum.TryParse alone isn't enough: for a non-[Flags] enum it also happily "succeeds" for any string
        // that parses as the underlying integer (e.g. "999999"), producing a value with no corresponding
        // named member - Enum.IsDefined is what actually rejects that.
        if (!Enum.TryParse<EGame>(options.UeVersion, out var game) || !Enum.IsDefined(game))
            throw new ArgumentException(
                $"Unknown UE version '{options.UeVersion}'. Use an exact EGame enum name from CUE4Parse, " +
                "e.g. GAME_UE5_3 (see https://github.com/FabianFG/CUE4Parse/blob/master/CUE4Parse/UE4/Versions/EGame.cs).");

        if (explicitContainerPaths == null && options.ScopeRules != null)
            ScopeClassifier.Validate(options.ScopeRules);

        EnsureOodleInitialized(onLog);
        cancellationToken.ThrowIfCancellationRequested();

        var versionContainer = new VersionContainer(game);
        DefaultFileProvider? provider = null;
        try {
            provider = new DefaultFileProvider(options.TargetFolder, SearchOption.AllDirectories, versionContainer, StringComparer.OrdinalIgnoreCase);

            if (explicitContainerPaths != null) {
                cancellationToken.ThrowIfCancellationRequested();
                provider.RegisterVfs(explicitContainerPaths.ToArray());
            } else if (options.ScopeRules == null) {
                cancellationToken.ThrowIfCancellationRequested();
                provider.Initialize();
            } else {
                // Deliberately skips Initialize() (CUE4Parse's own directory scan, which would register every
                // container unconditionally) and instead registers only the containers scope rules select -
                // this is what "apply scope before mounting" actually requires. RegisterVfs/Mount still do all
                // of CUE4Parse's own patch/DLC-precedence and IoStore pairing work exactly as normal; only which
                // containers get registered in the first place changes.
                var (containers, diagnostics) = ScopeContainerScanner.DiscoverAndClassify(
                    options.TargetFolder, options.ScopeRules, cancellationToken);
                foreach (var diagnostic in diagnostics)
                    onLog?.Invoke($"WARN: {diagnostic.Message}");

                var wanted = wantedActions ?? [ScopeAction.Game];
                var selected = containers.Where(c => wanted.Contains(c.Classification)).Select(c => c.AbsolutePath).ToArray();
                onLog?.Invoke($"Scope rules selected {selected.Length} of {containers.Count} container(s) for {string.Join("/", wanted)}.");
                provider.RegisterVfs(selected);
            }

            var keys = new Dictionary<FGuid, FAesKey>();
            foreach (var (guidHex, keyHex) in ResolveAesKeys(options))
                keys[new FGuid(guidHex)] = new FAesKey(keyHex);

            if (keys.Count > 0)
                provider.SubmitKeys(keys);

            cancellationToken.ThrowIfCancellationRequested();
            provider.Mount();
            cancellationToken.ThrowIfCancellationRequested();
            return provider;
        } catch {
            DisposePartiallyInitialized(provider, onLog);
            throw;
        }
    }

    /// <summary>A failure between allocating the provider and returning it (bad key, mount error, etc.) must not
    /// leak the partially-initialized provider - it can hold open file handles into the paks folder. Unload and
    /// dispose are attempted independently so a broken/partial VFS state can't stop disposal from happening.</summary>
    private static void DisposePartiallyInitialized(DefaultFileProvider? provider, Action<string>? onLog) {
        if (provider == null)
            return;

        try {
            provider.UnloadAllVfs();
        } catch (Exception ex) {
            onLog?.Invoke($"WARN: failed to unload the provider's VFS while cleaning up after an initialization failure: {ex.Message}");
        }

        try {
            provider.Dispose();
        } catch (Exception ex) {
            onLog?.Invoke($"WARN: failed to dispose the provider while cleaning up after an initialization failure: {ex.Message}");
        }
    }

    /// <summary>OodleHelper.Instance is a shared static, so two hashing operations initializing it at the same
    /// time (e.g. Hash Game Files and Hash Mod Files run from different GUI tabs concurrently) would otherwise
    /// race on the same one-time download/init. This is check-lock-check rather than a bare lock so the common
    /// already-initialized case doesn't pay for a lock on every provider creation.</summary>
    private static readonly object OodleInitLock = new();

    /// <summary>Most UE5 games compress paks with Oodle, which CUE4Parse can't bundle for licensing reasons.
    /// CUE4Parse's own helper downloads a known-good copy once and caches it for future runs.</summary>
    private static void EnsureOodleInitialized(Action<string>? onLog) {
        if (OodleHelper.Instance != null)
            return;

        lock (OodleInitLock) {
            if (OodleHelper.Instance != null)
                return;

            var dllPath = ResolveOodleDllPath();
            if (!File.Exists(dllPath))
                onLog?.Invoke("Downloading Oodle decompression library (one-time)...");

            OodleHelper.Initialize(dllPath);

            if (OodleHelper.Instance == null)
                throw new InvalidOperationException(
                    $"Failed to initialize Oodle decompression - the automatic download to '{dllPath}' may have " +
                    "failed. Check network access and that the destination folder is writable.");
        }
    }

    /// <summary>Prefers a copy already sitting beside the exe (e.g. hand-placed, or downloaded there on a prior
    /// run before the install folder became read-only) so an existing working setup keeps working unchanged.
    /// Otherwise falls back to a per-user cache folder: the exe's own folder is commonly a read-only install
    /// location (e.g. under Program Files), which would make the one-time auto-download fail with no actionable
    /// way to recover short of reinstalling elsewhere or running elevated.</summary>
    private static string ResolveOodleDllPath() {
        var besideExe = Path.Combine(AppContext.BaseDirectory, OodleHelper.OodleFileName);
        if (File.Exists(besideExe))
            return besideExe;

        var userCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DekUnrealGameAudit", "oodle");
        Directory.CreateDirectory(userCacheDir);
        return Path.Combine(userCacheDir, OodleHelper.OodleFileName);
    }

    private static IEnumerable<(string Guid, string Key)> ResolveAesKeys(ScanOptions options) {
        foreach (var kv in options.AesKeys)
            yield return kv;

        if (string.IsNullOrEmpty(options.AesFile))
            yield break;

        if (!File.Exists(options.AesFile))
            throw new FileNotFoundException($"AES key file not found: {options.AesFile}");

        var lineNumber = 0;
        foreach (var rawLine in File.ReadAllLines(options.AesFile)) {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var parts = line.Split('=', 2);
            if (parts.Length != 2)
                throw new FormatException(
                    $"Invalid line {lineNumber} in AES key file '{options.AesFile}' - expected guid=hexkey " +
                    "(the line's content is not included here since it may contain a real key).");

            yield return (parts[0].Trim(), parts[1].Trim());
        }
    }
}
