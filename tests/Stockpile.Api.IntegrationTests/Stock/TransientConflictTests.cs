using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using Stockpile.Application.Common.Exceptions;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Entities;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests.Stock;

/// <summary>
/// The backstop behind lock ordering: whatever still deadlocks (40P01) or fails
/// serialization (40001) must come out of the unit of work as a rolled-back, retryable
/// signal — not as the InvalidOperationException Npgsql's execution strategy wraps it in,
/// which the API could only report as a 500.
/// </summary>
[Collection(nameof(ApiCollection))]
public class TransientConflictTests(StockpileApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("40P01")]
    [InlineData("40001")]
    public async Task ATransientConflict_rollsBack_andIsSignalledForRetry(string sqlState)
    {
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync($"TC-{sqlState}");
        var warehouseId = await factory.SeedWarehouseAsync($"TC-WH-{sqlState}");
        await factory.SeedStockAsync(productId, warehouseId, 10);

        using var scope = factory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var writer = scope.ServiceProvider.GetRequiredService<IStockWriter>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var thrown = await Should.ThrowAsync<TransientConflictException>(() =>
            unitOfWork.ExecuteInTransactionAsync<bool>(async ct =>
            {
                await writer.TryAdjustAsync(productId, warehouseId, -4, ct);
                db.Customers.Add(Customer.Create("Doomed SA", null, null, null).Value);

                throw new PostgresException("simulated", "ERROR", "ERROR", sqlState);
            }, Ct));

        thrown.SqlState.ShouldBe(sqlState);
        db.ChangeTracker.Entries().ShouldBeEmpty();
        (await factory.ReadStockAsync(productId, warehouseId)).OnHand.ShouldBe(10);
    }

    [Fact]
    public async Task ADeadlockRaisedAtSaveChanges_isSignalledForRetry()
    {
        // The other place Postgres can pick this transaction as the victim: the flush of
        // the ledger rows, where it arrives wrapped in a DbUpdateException.
        await factory.ResetDatabaseAsync();

        using var scope = factory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await Should.ThrowAsync<TransientConflictException>(() =>
            unitOfWork.ExecuteInTransactionAsync<bool>(async ct =>
            {
                // Makes the flush trip over a simulated deadlock.
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE OR REPLACE FUNCTION tc_fail_deadlock() RETURNS trigger AS $$
                    BEGIN RAISE EXCEPTION 'simulated' USING ERRCODE = '40P01'; END $$ LANGUAGE plpgsql;
                    CREATE TRIGGER fail_deadlock BEFORE INSERT ON customers
                        FOR EACH ROW EXECUTE FUNCTION tc_fail_deadlock();
                    """, ct);

                db.Customers.Add(Customer.Create("Doomed SA", null, null, null).Value);
                return true;
            }, Ct));

        db.ChangeTracker.Entries().ShouldBeEmpty();
    }
}
