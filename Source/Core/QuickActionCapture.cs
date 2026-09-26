namespace DekUnrealGameAudit.Core;

/// <summary>The three capture stages (Game/Mods/optional Header) shared identically by
/// <see cref="BeforeUpdateOrchestrator"/> and <see cref="AfterUpdateOrchestrator"/> - both need to produce a
/// fresh snapshot the same way, just at different points in the workflow and with different consequences
/// (Before decides whether to activate a baseline; After feeds the comparisons).</summary>
internal static class QuickActionCapture {
    internal delegate GameManifest GameCapture(ScanOptions options, Action<string>? onLog, CancellationToken cancellationToken);
    internal delegate ModManifest ModsCapture(ScanOptions options, Action<string>? onLog, CancellationToken cancellationToken);
    internal delegate HeaderManifest HeaderCapture(string folder, Action<string>? onLog, CancellationToken cancellationToken);

    public static RunRecord CaptureAll(QuickActionOptions options, string runDirectory, Action<string> log,
        CancellationToken cancellationToken, GameCapture? captureGame = null, ModsCapture? captureMods = null,
        HeaderCapture? captureHeaders = null) {
        captureGame ??= static (scanOptions, onLog, token) =>
            GameHasher.Hash(scanOptions, onLog: onLog, cancellationToken: token);
        captureMods ??= static (scanOptions, onLog, token) =>
            ModsHasher.Hash(scanOptions, onLog: onLog, cancellationToken: token);
        captureHeaders ??= static (folder, onLog, token) =>
            HeaderHasher.Hash(folder, onLog: onLog, cancellationToken: token);
        var record = new RunRecord {
            RunId = Path.GetFileName(runDirectory),
            CreatedAt = DateTime.UtcNow.ToString("o"),
            UeVersion = options.UeVersion,
            ScopeFingerprint = options.ScopeRules != null ? ScopeClassifier.ComputeFingerprint(options.ScopeRules) : null,
            ProfileLabel = options.ProfileLabel,
        };

        log("=== Capturing game manifest ===");
        try {
            cancellationToken.ThrowIfCancellationRequested();
            var gameManifest = captureGame(
                new ScanOptions {
                    TargetFolder = options.PaksFolder,
                    UeVersion = options.UeVersion,
                    AesKeys = options.AesKeys,
                    AesFile = options.AesFile,
                    ScopeRules = options.ScopeRules,
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                }, log, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(runDirectory, "game-manifest.json");
            JsonUtil.WriteFile(path, gameManifest);
            record.GameManifestPath = path;
            record.GameStage = StageStatus.Completed;
        } catch (OperationCanceledException) {
            record.GameStage = StageStatus.Failed;
            record.Notes.Add("Game capture was cancelled.");
            log("Game capture was cancelled.");
        } catch (Exception ex) {
            record.GameStage = StageStatus.Failed;
            record.Notes.Add($"Game capture failed: {ex.Message}");
            log($"WARN: game capture failed: {ex.Message}");
        }

        log("=== Capturing mod inventory ===");
        if (cancellationToken.IsCancellationRequested) {
            record.ModsStage = StageStatus.Skipped;
            record.Notes.Add("Mods capture skipped - cancelled before it started.");
            log("Mods capture skipped - cancelled before it started.");
        } else {
            try {
                var modManifest = captureMods(
                    new ScanOptions {
                        TargetFolder = options.ModsFolder,
                        UeVersion = options.UeVersion,
                        AesKeys = options.AesKeys,
                        AesFile = options.AesFile,
                        ScopeRules = options.ScopeRules,
                    }, log, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(runDirectory, "mods-manifest.json");
                JsonUtil.WriteFile(path, modManifest);
                record.ModsManifestPath = path;
                record.ModsStage = StageStatus.Completed;
            } catch (OperationCanceledException) {
                record.ModsStage = StageStatus.Failed;
                record.Notes.Add("Mods capture was cancelled.");
                log("Mods capture was cancelled.");
            } catch (Exception ex) {
                record.ModsStage = StageStatus.Failed;
                record.Notes.Add($"Mods capture failed: {ex.Message}");
                log($"WARN: mods capture failed: {ex.Message}");
            }
        }

        if (options.HeaderFolder == null) {
            record.HeaderStage = StageStatus.Skipped;
        } else if (cancellationToken.IsCancellationRequested) {
            record.HeaderStage = StageStatus.Skipped;
            record.Notes.Add("Header capture skipped - cancelled before it started.");
            log("Header capture skipped - cancelled before it started.");
        } else {
            log("=== Capturing header manifest ===");
            try {
                var headerManifest = captureHeaders(options.HeaderFolder, log, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var path = Path.Combine(runDirectory, "header-manifest.json");
                JsonUtil.WriteFile(path, headerManifest);
                record.HeaderManifestPath = path;
                record.HeaderStage = StageStatus.Completed;
            } catch (OperationCanceledException) {
                record.HeaderStage = StageStatus.Failed;
                record.Notes.Add("Header capture was cancelled.");
                log("Header capture was cancelled.");
            } catch (Exception ex) {
                record.HeaderStage = StageStatus.Failed;
                record.Notes.Add($"Header capture failed: {ex.Message}");
                log($"WARN: header capture failed: {ex.Message}");
            }
        }

        return record;
    }
}
