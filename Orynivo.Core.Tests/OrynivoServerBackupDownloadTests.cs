using Orynivo.Streaming;
using Xunit;

namespace Orynivo.Core.Tests;

/// <summary>Verifies atomic publication of downloaded Orynivo Server backups.</summary>
public sealed class OrynivoServerBackupDownloadTests
{
    /// <summary>Ensures the temporary output stream is closed before the file is renamed on Windows.</summary>
    [Fact]
    public async Task WriteDownloadAtomicallyAsync_ClosesTemporaryFileBeforeRename()
    {
        var root = Path.Combine(Path.GetTempPath(), $"orynivo-backup-download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var destinationPath = Path.Combine(root, "backup.zip");
        var expected = new byte[] { 1, 2, 3, 4, 5 };

        try
        {
            await using var input = new MemoryStream(expected);
            await OrynivoServerClient.WriteDownloadAtomicallyAsync(
                input,
                destinationPath,
                expected.Length,
                progress: null,
                CancellationToken.None);

            Assert.Equal(expected, await File.ReadAllBytesAsync(destinationPath));
            Assert.False(File.Exists(destinationPath + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
