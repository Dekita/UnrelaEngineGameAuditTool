using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Commands;

public static class HashHeaderFilesCommand {
    public static int Run(ArgMap flags, CancellationToken cancellationToken = default) {
        flags.EnsureKnownKeys("ue4ss", "out");

        var ue4ssFolder = flags.RequireOne("ue4ss");
        var outPath = flags.OneOrDefault("out");

        if (!string.IsNullOrWhiteSpace(outPath))
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);

        Console.WriteLine($"Parsing UE4SS header dumps from: {ue4ssFolder}");
        var manifest = HeaderHasher.Hash(Path.GetFullPath(ue4ssFolder), onLog: Console.WriteLine,
            cancellationToken: cancellationToken);

        var finalOutPath = !string.IsNullOrWhiteSpace(outPath)
            ? outPath
            : AutoFileNaming.AllocateUniquePath(HeaderHasher.BuildAutoFileName);
        cancellationToken.ThrowIfCancellationRequested();
        JsonUtil.WriteFile(finalOutPath, manifest);

        Console.WriteLine($"CXX types: {manifest.CxxTypes.Count}   CXX enums: {manifest.CxxEnums.Count}   UHT types: {manifest.UhtTypes.Count}");
        Console.WriteLine($"Wrote header manifest to {Path.GetFullPath(finalOutPath)}");
        return 0;
    }
}
