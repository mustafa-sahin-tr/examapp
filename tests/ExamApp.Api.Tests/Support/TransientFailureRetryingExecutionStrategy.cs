using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace ExamApp.Api.Tests.Support;

/// <summary>Simulated transient database failure (stands in for a dropped connection / Npgsql transient error).</summary>
public sealed class TransientTestException(string message) : Exception(message);

/// <summary>
/// issue #277 takip: like <see cref="TestRetryingExecutionStrategy"/> but actually RETRIES when the failure is a
/// <see cref="TransientTestException"/> — reproduces Npgsql retry-on-failure re-running the whole delegate.
/// </summary>
public sealed class TransientFailureRetryingExecutionStrategy : ExecutionStrategy
{
    public TransientFailureRetryingExecutionStrategy(ExecutionStrategyDependencies dependencies)
        : base(dependencies, maxRetryCount: 3, maxRetryDelay: TimeSpan.FromMilliseconds(1))
    {
    }

    protected override bool ShouldRetryOn(Exception exception) => exception is TransientTestException;
}

/// <summary>
/// Throws <see cref="TransientTestException"/> on the first <c>COMMIT</c> (before it reaches the database): all
/// SaveChanges calls of that attempt succeeded, then the transaction is rolled back — the classic "lost rows on retry" case.
/// </summary>
public sealed class FailFirstCommitInterceptor : DbTransactionInterceptor
{
    public int Failures { get; private set; }

    public override ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        if (Failures == 0)
        {
            Failures++;
            throw new TransientTestException("simulated transient failure at commit");
        }

        return ValueTask.FromResult(result);
    }
}
