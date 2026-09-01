using BadgeService;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Tests.Support;

/// <summary>Isolated in-memory SQLite <see cref="BadgeDbContext"/>.</summary>
public sealed class BadgeTestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    private BadgeTestDb(SqliteConnection connection) => _connection = connection;

    public static BadgeTestDb Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using (var ctx = new BadgeDbContext(Options(connection)))
            ctx.Database.EnsureCreated();
        return new BadgeTestDb(connection);
    }

    public BadgeDbContext NewContext() => new(Options(_connection));

    private static DbContextOptions<BadgeDbContext> Options(SqliteConnection connection) =>
        new DbContextOptionsBuilder<BadgeDbContext>().UseSqlite(connection).Options;

    public void Dispose() => _connection.Dispose();
}
