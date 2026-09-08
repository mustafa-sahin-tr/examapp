using ExamApp.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AuthApi.Tests.Support;

/// <summary>
/// A fresh <see cref="AppDbContext"/> (auth-api's own context) backed by an isolated
/// in-memory SQLite database — mirrors the pattern used by ExamApp.Api.Tests/Support/TestDb.cs
/// and BadgeService.Tests/Support/BadgeTestDb.cs.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    private TestDb(SqliteConnection connection) => _connection = connection;

    public static TestDb Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        using (var ctx = new AppDbContext(Options(connection)))
        {
            ctx.Database.EnsureCreated();
        }

        return new TestDb(connection);
    }

    public AppDbContext NewContext() => new(Options(_connection));

    private static DbContextOptions<AppDbContext> Options(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging()
            .Options;

    public void Dispose() => _connection.Dispose();
}
