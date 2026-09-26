using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>CODE-04 (header-coverage half): HeaderHasher.Hash must record whether each dump folder was
/// actually found, not just leave the manifest's dictionaries empty either way - real local filesystem
/// checks, no CUE4Parse/provider involved, so unlike GameHasher this is directly testable with temp folders.</summary>
public class HeaderHasherTests {
    private static string NewTempFolder() {
        var path = Path.Combine(Path.GetTempPath(), "DekUnrealGameAuditTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void Hash_OnlyCxxDumpPresent_RecordsCxxFoundAndUhtNotFound() {
        var root = NewTempFolder();
        try {
            Directory.CreateDirectory(Path.Combine(root, "CXXHeaderDump"));

            var manifest = HeaderHasher.Hash(root);

            Assert.True(manifest.CxxDumpFound);
            Assert.False(manifest.UhtDumpFound);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Hash_OnlyUhtDumpPresent_RecordsUhtFoundAndCxxNotFound() {
        var root = NewTempFolder();
        try {
            Directory.CreateDirectory(Path.Combine(root, "UHTHeaderDump"));

            var manifest = HeaderHasher.Hash(root);

            Assert.True(manifest.UhtDumpFound);
            Assert.False(manifest.CxxDumpFound);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Hash_BothDumpsPresent_RecordsBothFound() {
        var root = NewTempFolder();
        try {
            Directory.CreateDirectory(Path.Combine(root, "CXXHeaderDump"));
            Directory.CreateDirectory(Path.Combine(root, "UHTHeaderDump"));

            var manifest = HeaderHasher.Hash(root);

            Assert.True(manifest.CxxDumpFound);
            Assert.True(manifest.UhtDumpFound);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Hash_NeitherDumpPresent_Throws() {
        var root = NewTempFolder();
        try {
            Assert.Throws<InvalidOperationException>(() => HeaderHasher.Hash(root));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }
}
