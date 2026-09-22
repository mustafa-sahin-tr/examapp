using ExamApp.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Support;

/// <summary>
/// A fresh <see cref="AppDbContext"/> backed by an isolated in-memory SQLite
/// database (real relational behaviour, unlike the EF InMemory provider).
/// The connection is kept open for the lifetime of the handle so the schema
/// survives; dispose the handle to drop the database.
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

    /// <summary>A new context on the same database — mirrors a fresh request scope.</summary>
    public AppDbContext NewContext() => new(Options(_connection));

    /// <summary>
    /// A new context with EF interceptors attached — e.g. a <c>SaveChangesInterceptor</c> that simulates a
    /// concurrent writer between "query" and "save" (race conditions that unit tests can't otherwise reach).
    /// </summary>
    public AppDbContext NewContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
        => new(Options(_connection, interceptors));

    private static DbContextOptions<AppDbContext> Options(
        SqliteConnection connection, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .EnableSensitiveDataLogging();

        if (interceptors.Length > 0)
            builder.AddInterceptors(interceptors);

        return builder.Options;
    }

    public void Dispose() => _connection.Dispose();
}
