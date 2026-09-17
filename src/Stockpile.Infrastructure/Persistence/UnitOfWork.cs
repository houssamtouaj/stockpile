using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Infrastructure.Persistence;

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

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return result;
        }, cancellationToken);
    }
}
