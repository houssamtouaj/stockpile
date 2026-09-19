using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests.Stock;

[Collection(nameof(ApiCollection))]
public class StockWriterTests(StockpileApiFactory factory)
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Guid ProductId, Guid WarehouseId)> ArrangeAsync(int onHand, long cost = 1000)
    {
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("SW-001");
        var warehouseId = await factory.SeedWarehouseAsync("SW-WH");
        if (onHand >= 0)
            await factory.SeedStockAsync(productId, warehouseId, onHand, cost);
        return (productId, warehouseId);
    }

    [Fact]
    public async Task Reserve_whenAvailable_appliesAndReturnsThePostUpdateValues()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var result = await factory.WithWriterAsync((writer, _) =>
            writer.TryReserveAsync(productId, warehouseId, 4, Ct));

        result.IsApplied.ShouldBeTrue();
        result.OnHandAfter.ShouldBe(10);
        result.ReservedAfter.ShouldBe(4);

        var stock = await factory.ReadStockAsync(productId, warehouseId);
        stock.OnHand.ShouldBe(10);
        stock.Reserved.ShouldBe(4);
    }

    [Fact]
    public async Task Reserve_beyondAvailable_reportsInsufficient_andChangesNothing()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 3);

        var result = await factory.WithWriterAsync((writer, _) =>
            writer.TryReserveAsync(productId, warehouseId, 4, Ct));

        result.IsInsufficient.ShouldBeTrue();
        (await factory.ReadStockAsync(productId, warehouseId)).Reserved.ShouldBe(0);
    }

    [Fact]
    public async Task Reserve_whenNoStockRowExists_reportsRowMissing()
    {
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("SW-002");
        var warehouseId = await factory.SeedWarehouseAsync("SW-WH2");

        var result = await factory.WithWriterAsync((writer, _) =>
            writer.TryReserveAsync(productId, warehouseId, 1, Ct));

        result.IsRowMissing.ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureStockItem_isIdempotent()
    {
        await factory.ResetDatabaseAsync();
        var productId = await factory.SeedProductAsync("SW-003");
        var warehouseId = await factory.SeedWarehouseAsync("SW-WH3");

        await factory.WithWriterAsync(async (writer, _) =>
        {
            await writer.EnsureStockItemAsync(productId, warehouseId, Ct);
            await writer.EnsureStockItemAsync(productId, warehouseId, Ct);
            return true;
        });

        var stock = await factory.ReadStockAsync(productId, warehouseId);
        stock.OnHand.ShouldBe(0);
    }

    [Fact]
    public async Task Issue_dropsBothCounters_andReportsThePreviousOnHand()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);
        await factory.WithWriterAsync((w, _) => w.TryReserveAsync(productId, warehouseId, 6, Ct));

        var result = await factory.WithWriterAsync((w, _) =>
            w.TryIssueAsync(productId, warehouseId, 6, Ct));

        result.IsApplied.ShouldBeTrue();
        result.OnHandAfter.ShouldBe(4);
        result.ReservedAfter.ShouldBe(0);
        result.PreviousOnHand.ShouldBe(10);
    }

    [Fact]
    public async Task Issue_withoutEnoughReserved_isRefused()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var result = await factory.WithWriterAsync((w, _) =>
            w.TryIssueAsync(productId, warehouseId, 2, Ct));

        result.IsInsufficient.ShouldBeTrue();
    }

    [Fact]
    public async Task Receive_recomputesTheWeightedAverageCostInSql()
    {
        // 10 at 500 = 5000; receive 10 at 700 = 7000; 12000 / 20 = 600.
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10, cost: 500);

        var result = await factory.WithWriterAsync((w, _) =>
            w.TryReceiveAsync(productId, warehouseId, 10, 700, Ct));

        result.OnHandAfter.ShouldBe(20);
        (await factory.ReadStockAsync(productId, warehouseId)).AverageCost.ShouldBe(600);
    }

    [Fact]
    public async Task Receive_roundsRatherThanTruncating()
    {
        // 3 at 100 = 300; receive 1 at 101 = 101; 401 / 4 = 100.25 -> 100.
        var (productId, warehouseId) = await ArrangeAsync(onHand: 3, cost: 100);

        await factory.WithWriterAsync((w, _) => w.TryReceiveAsync(productId, warehouseId, 1, 101, Ct));

        (await factory.ReadStockAsync(productId, warehouseId)).AverageCost.ShouldBe(100);
    }

    [Fact]
    public async Task Adjust_downwardBelowReserved_isRefused()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);
        await factory.WithWriterAsync((w, _) => w.TryReserveAsync(productId, warehouseId, 8, Ct));

        var result = await factory.WithWriterAsync((w, _) =>
            w.TryAdjustAsync(productId, warehouseId, -5, Ct));

        result.IsInsufficient.ShouldBeTrue();
        (await factory.ReadStockAsync(productId, warehouseId)).OnHand.ShouldBe(10);
    }

    [Fact]
    public async Task Count_reportsBothThePreviousAndTheObservedQuantity()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var result = await factory.WithWriterAsync((w, _) =>
            w.TryCountAsync(productId, warehouseId, observedOnHand: 7, countedAt: Now, ct: Ct));

        result.IsApplied.ShouldBeTrue();
        result.PreviousOnHand.ShouldBe(10);
        result.OnHandAfter.ShouldBe(7);
    }

    [Fact]
    public async Task Count_belowReserved_isRefused()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);
        await factory.WithWriterAsync((w, _) => w.TryReserveAsync(productId, warehouseId, 8, Ct));

        var result = await factory.WithWriterAsync((w, _) =>
            w.TryCountAsync(productId, warehouseId, observedOnHand: 5, countedAt: Now, ct: Ct));

        result.IsInsufficient.ShouldBeTrue();
    }

    [Fact]
    public async Task Writer_calledOutsideATransaction_throwsWithAnActionableMessage()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);

        using var scope = factory.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IStockWriter>();

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            writer.TryReserveAsync(productId, warehouseId, 1, Ct));

        exception.Message.ShouldContain("outside a transaction");
    }

    [Fact]
    public async Task SnapshotWriteAndLedgerAppend_rollBackTogether()
    {
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);

        // Reserve, then throw before the transaction commits.
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await factory.WithWriterAsync<bool>(async (writer, _) =>
            {
                var write = await writer.TryReserveAsync(productId, warehouseId, 4, Ct);
                write.IsApplied.ShouldBeTrue();
                throw new InvalidOperationException("simulated failure after the write");
            }));

        // If the writer had its own connection, reserved would now be 4.
        (await factory.ReadStockAsync(productId, warehouseId)).Reserved.ShouldBe(0);
    }

    [Fact]
    public async Task AFailedResult_rollsBackEverythingTheHandlerAlreadyWrote()
    {
        // The Result-failure sibling of the test above, and the one people miss: a
        // handler that refuses does NOT throw, so the using-scoped transaction never
        // rolls back on its own. UnitOfWork has to inspect the Result and roll back
        // explicitly (phase 01 task 11 step 6). Without that check the reserve commits.
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);

        var outcome = await factory.WithWriterAsync<Result>(async (writer, _) =>
        {
            var write = await writer.TryReserveAsync(productId, warehouseId, 4, Ct);
            write.IsApplied.ShouldBeTrue();
            return Result.Fail(new DomainRuleError("test.refused", "simulated refusal"));
        });

        outcome.IsFailure.ShouldBeTrue();
        (await factory.ReadStockAsync(productId, warehouseId)).Reserved.ShouldBe(0);
    }

    [Fact]
    public async Task LedgerTrigger_refusesAnUpdateToAnExistingMovement()
    {
        var ct = Ct;
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO stock_movements
                (id, product_id, warehouse_id, type, on_hand_delta, reserved_delta,
                 on_hand_after, reserved_after, unit_cost_cents, reference_type,
                 reference_id, idempotency_key, reason, occurred_at, performed_by_user_id)
            VALUES (gen_random_uuid(), {0}, {1}, 6, 0, 1, 10, 1, NULL, 'Test',
                    NULL, {2}, NULL, now(), gen_random_uuid());
            """,
            [productId, warehouseId, $"trigger-test-{Guid.CreateVersion7()}"], ct);

        var exception = await Should.ThrowAsync<Exception>(() =>
            db.Database.ExecuteSqlRawAsync(
                "UPDATE stock_movements SET reason = 'tampered';", ct));

        exception.ToString().ShouldContain("append-only");
    }

    [Fact]
    public async Task LedgerTrigger_refusesADeleteOfAnExistingMovement()
    {
        var ct = Ct;
        var (productId, warehouseId) = await ArrangeAsync(onHand: 10);

        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO stock_movements
                (id, product_id, warehouse_id, type, on_hand_delta, reserved_delta,
                 on_hand_after, reserved_after, unit_cost_cents, reference_type,
                 reference_id, idempotency_key, reason, occurred_at, performed_by_user_id)
            VALUES (gen_random_uuid(), {0}, {1}, 6, 0, 1, 10, 1, NULL, 'Test',
                    NULL, {2}, NULL, now(), gen_random_uuid());
            """,
            [productId, warehouseId, $"trigger-delete-{Guid.CreateVersion7()}"], ct);

        await Should.ThrowAsync<Exception>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM stock_movements;", ct));
    }
}
