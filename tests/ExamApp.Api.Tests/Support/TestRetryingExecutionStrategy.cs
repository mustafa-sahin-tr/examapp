using System;
using Microsoft.EntityFrameworkCore.Storage;

namespace ExamApp.Api.Tests.Support;

/// <summary>
/// issue #279 review (critical fix regression guard): a minimal <see cref="ExecutionStrategy"/> with
/// <c>RetriesOnFailure = true</c> (the base class default — never overridden here), used to reproduce, on
/// SQLite, the same failure mode Npgsql's retry-on-failure (enabled by Aspire's
/// <c>AddNpgsqlDbContext&lt;AppDbContext&gt;</c>, see <c>Program.cs</c>) causes in production: EF Core
/// refuses to execute a command inside a user-initiated transaction (<c>Database.BeginTransactionAsync()</c>
/// called OUTSIDE <c>Database.CreateExecutionStrategy().ExecuteAsync(...)</c>) once a retrying strategy is
/// configured — <c>InvalidOperationException("... does not support user-initiated transactions ...")</c>.
///
/// SQLite's own default execution strategy never retries, so this failure mode is otherwise invisible to
/// SQLite-backed unit tests — attaching THIS strategy to a test context is what makes a regression (a
/// service method opening <c>BeginTransactionAsync()</c> outside <c>CreateExecutionStrategy().ExecuteAsync</c>)
/// fail loudly in a fast unit test instead of only in production against Postgres.
/// </summary>
public sealed class TestRetryingExecutionStrategy : ExecutionStrategy
{
    public TestRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
    }

    // No transient failures are simulated — RetriesOnFailure being true (inherited, never false) is what
    // matters for reproducing the "existing transaction" guard; retry behavior itself isn't exercised here.
    protected override bool ShouldRetryOn(Exception exception) => false;
}
