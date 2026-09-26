using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Stockpile.Application.Common.Exceptions;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Infrastructure.Persistence;

/// <summary>
/// Raised when a stock CHECK constraint fires. It should never happen: the conditional
/// UPDATEs in <see cref="StockWriter"/> refuse anything that would breach an invariant, so
/// reaching the constraint means a predicate has a hole. Translated to a 500 plus a loud
/// log line by the exception handler, never to a quiet 422.
/// </summary>
public sealed class StockInvariantViolatedException(string constraintName)
    : Exception($"Stock invariant '{constraintName}' was violated. This indicates a defect.")
{
    public string ConstraintName { get; } = constraintName;
}

public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        // Already inside one (nested handler, or a test that opened its own): join it.
        if (db.Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        // CreateExecutionStrategy is what makes this safe to combine with Npgsql's
        // retry-on-transient-failure. Calling BeginTransactionAsync directly under a
        // retrying strategy throws at runtime with a message most people have to search for.
        var strategy = db.Database.CreateExecutionStrategy();

        try
        {
            return await ExecuteOnceAsync(strategy, operation, cancellationToken);
        }
        catch (Exception ex) when (IsTransientConflict(ex, out var sqlState))
        {
            // Postgres chose this transaction as a deadlock victim, or refused to serialize
            // it. The transaction was rolled back as it was disposed on the way out, so
            // nothing it wrote survives; the tracked entities describe that dead attempt and
            // must not leak into the retry. TransactionBehavior re-runs the operation, the
            // same way it replays a lost idempotency race.
            db.ChangeTracker.Clear();
            throw new TransientConflictException(sqlState, ex);
        }
    }

    private async Task<T> ExecuteOnceAsync<T>(
        IExecutionStrategy strategy,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var result = await operation(ct);

            // A failed Result is a refusal, not an exception — but it must still roll
            // back. Handlers routinely mutate state before discovering the refusal: a
            // multi-line sales-order confirm reserves line 1 before line 2 comes up
            // short, and a cycle count writes last_counted_at before the zero-delta
            // guard fires. Committing here would leave those partial writes behind and
            // silently break the all-or-nothing contract phase 03 asserts.
            //
            // DO NOT REMOVE. Three tests depend on it: AdjustAndCountTests
            // .Count_toTheSameQuantity_... (phase 02), SalesOrderTests
            // .Confirm_withInsufficientStockOnOneLine_reservesNothing (phase 03), and the
            // transfer legs' all-or-nothing behaviour. Without it they fail in a way that
            // looks like a stock bug and is actually a transaction bug.
            if (result is Result { IsFailure: true })
            {
                await transaction.RollbackAsync(ct);
                return result;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsIdempotencyReplay(ex, out var replayedKeyIndex))
            {
                // A concurrent request with the same idempotency key won the race and
                // already applied this mutation. Everything this attempt computed is now
                // void: `result` carries the after-values returned by a conditional UPDATE
                // that has just been rolled back, so handing it back would report a
                // quantity that never existed — and report it as a fresh application.
                //
                // Discard the tracked state and signal a replay instead. TransactionBehavior
                // re-runs the operation, whose idempotency fast path now finds the winner's
                // committed movement and returns ITS recorded after-values. That is the only
                // answer that was ever true, and the only one the caller can act on.
                await transaction.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                throw new IdempotencyReplayException(replayedKeyIndex);
            }
            catch (DbUpdateException ex) when (IsStockInvariantViolation(ex, out var constraint))
            {
                await transaction.RollbackAsync(ct);
                throw new StockInvariantViolatedException(constraint);
            }

            await transaction.CommitAsync(ct);

            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// 40P01 (deadlock_detected) or 40001 (serialization_failure), wherever it sits in the
    /// chain: raised directly by a raw stock statement, inside the DbUpdateException of a
    /// failed flush, or inside the InvalidOperationException Npgsql's execution strategy
    /// wraps anything transient in ("likely due to a transient failure").
    /// </summary>
    private static bool IsTransientConflict(Exception exception, out string sqlState)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "40P01" or "40001" } pg)
            {
                sqlState = pg.SqlState;
                return true;
            }
        }

        sqlState = string.Empty;
        return false;
    }

    private static bool IsIdempotencyReplay(DbUpdateException exception, out string constraint)
    {
        if (exception.InnerException is PostgresException { SqlState: "23505" } pg
            && pg.ConstraintName?.Contains("idempotency_key", StringComparison.OrdinalIgnoreCase) == true)
        {
            constraint = pg.ConstraintName;
            return true;
        }

        constraint = string.Empty;
        return false;
    }

    private static bool IsStockInvariantViolation(DbUpdateException exception, out string constraint)
    {
        if (exception.InnerException is PostgresException { SqlState: "23514" } pg)
        {
            constraint = pg.ConstraintName ?? "unknown";
            return true;
        }

        constraint = string.Empty;
        return false;
    }
}
