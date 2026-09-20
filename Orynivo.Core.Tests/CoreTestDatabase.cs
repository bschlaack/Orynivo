using Microsoft.Data.Sqlite;
using Orynivo.Library;

namespace Orynivo.Core.Tests;

/// <summary>
/// Owns an isolated SQLite library database for one test. Every instance gets its
/// own temporary directory, so a test can never observe another test's rows or
/// delete a database another test is still using, even while xUnit runs test
/// classes in parallel.
/// </summary>
internal sealed class CoreTestDatabase : IDisposable
{
    private CoreTestDatabase(string directory)
    {
        Directory = directory;
        DatabasePath = Path.Combine(directory, "library.db");
    }

    /// <summary>Gets the temporary directory owned by this instance.</summary>
    internal string Directory { get; }

    /// <summary>Gets the isolated database file path.</summary>
    internal string DatabasePath { get; }

    /// <summary>Creates a unique temporary database directory for one test.</summary>
    /// <param name="prefix">Short label included in the directory name for diagnostics.</param>
    /// <returns>The created instance; disposing it removes the directory.</returns>
    internal static CoreTestDatabase Create(string prefix = "orynivo-test")
    {
        var directory = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(directory);
        return new CoreTestDatabase(directory);
    }

    /// <summary>Opens the isolated library database.</summary>
    /// <returns>A database bound to this instance's file.</returns>
    internal AudioDatabase Open() => new(DatabasePath);

    /// <summary>Builds an absolute path inside the isolated directory, for example a fake media file.</summary>
    /// <param name="parts">Path segments below the isolated directory.</param>
    /// <returns>The combined absolute path.</returns>
    internal string PathOf(params string[] parts) =>
        Path.Combine([Directory, .. parts]);

    /// <summary>
    /// Closes the pooled connections of one isolated database so its file can be
    /// deleted. Prefer this over <see cref="SqliteConnection.ClearAllPools"/>,
    /// which would also close pooled connections of tests running in parallel in
    /// other collections.
    /// </summary>
    /// <param name="databasePath">Isolated database file path.</param>
    internal static void ClearPool(string databasePath)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            SqliteConnection.ClearPool(connection);
        }
        catch (Exception)
        {
            // A pool that cannot be cleared must not hide the test result.
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        ClearPool(DatabasePath);

        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is harmless.
        }
    }
}
