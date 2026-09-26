using DekUnrealGameAudit.Core;

namespace DekUnrealGameAudit.Tests;

/// <summary>Regression coverage for CODE-03 (see IMPROVEMENT-PLAN.md): every manifest/report/settings write
/// goes through AtomicFile so a failure partway through a write can never truncate the last good file on disk.</summary>
public class AtomicFileTests {
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"atomic-file-test-{Guid.NewGuid():N}.txt");

    [Fact]
    public void WriteAllText_CreatesNewFileWithExactContent() {
        var path = TempPath();
        try {
            AtomicFile.WriteAllText(path, "hello");
            Assert.Equal("hello", File.ReadAllText(path));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteAllText_ReplacesExistingFileContentCompletely() {
        var path = TempPath();
        try {
            File.WriteAllText(path, "a much longer original value that should be fully replaced");
            AtomicFile.WriteAllText(path, "short");
            Assert.Equal("short", File.ReadAllText(path));
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteAllText_LeavesNoTempFileBehindOnSuccess() {
        var path = TempPath();
        var dir = Path.GetDirectoryName(path)!;
        try {
            AtomicFile.WriteAllText(path, "content");
            var leftovers = Directory.GetFiles(dir, $".{Path.GetFileName(path)}.*.tmp");
            Assert.Empty(leftovers);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteViaStream_FailurePartwayThroughPreservesTheOldFileAndLeavesNoTempFile() {
        var path = TempPath();
        var dir = Path.GetDirectoryName(path)!;
        try {
            File.WriteAllText(path, "the last good file");

            Assert.Throws<InvalidOperationException>(() => AtomicFile.WriteViaStream(path, stream => {
                stream.Write("partial"u8);
                stream.Flush();
                throw new InvalidOperationException("simulated failure mid-write");
            }));

            Assert.Equal("the last good file", File.ReadAllText(path));
            var leftovers = Directory.GetFiles(dir, $".{Path.GetFileName(path)}.*.tmp");
            Assert.Empty(leftovers);
        } finally {
            File.Delete(path);
        }
    }

    [Fact]
    public void WriteAllText_FailureBeforeAnyDestinationExistsLeavesNothingBehind() {
        var path = TempPath();
        var dir = Path.GetDirectoryName(path)!;
        try {
            Assert.Throws<InvalidOperationException>(() => AtomicFile.WriteViaStream(path, _ =>
                throw new InvalidOperationException("simulated failure before any bytes are written")));

            Assert.False(File.Exists(path));
            var leftovers = Directory.GetFiles(dir, $".{Path.GetFileName(path)}.*.tmp");
            Assert.Empty(leftovers);
        } finally {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void WriteAllText_SuccessiveCallsDoNotCollideOnTempNames() {
        var path = TempPath();
        try {
            for (var i = 0; i < 5; i++)
                AtomicFile.WriteAllText(path, $"version {i}");

            Assert.Equal("version 4", File.ReadAllText(path));
        } finally {
            File.Delete(path);
        }
    }
}
