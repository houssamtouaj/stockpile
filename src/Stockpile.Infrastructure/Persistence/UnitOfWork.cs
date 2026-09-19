using Microsoft.EntityFrameworkCore;
using Npgsql;
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
            catch (DbUpdateException ex) when (IsIdempotencyReplay(ex))
            {
                // A concurrent request with the same idempotency key won the race and
                // already applied this mutation. Roll ours back and report success:
                // the caller's intent has been satisfied exactly once, which is the
                // contract an idempotency key promises.
                await transaction.RollbackAsync(ct);
                return result;
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

    private static bool IsIdempotencyReplay(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: "23505" } pg
        && pg.ConstraintName?.Contains("idempotency_key", StringComparison.OrdinalIgnoreCase) == true;

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
