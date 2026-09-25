using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Entities;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.IntegrationTests.Orders;

[Collection(nameof(ApiCollection))]
public class OrderPersistenceTests(StockpileApiFactory factory)
{
    [Fact]
    public async Task DocumentNumbers_areSequentialPerPrefix_andCarryTheYear()
    {
        await factory.ResetDatabaseAsync();
        using var scope = factory.CreateScope();
        var numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>();
        var year = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow.Year;

        var ct = TestContext.Current.CancellationToken;

        (await numbers.NextAsync("PO", ct)).ShouldBe($"PO-{year}-00001");
        (await numbers.NextAsync("PO", ct)).ShouldBe($"PO-{year}-00002");
        (await numbers.NextAsync("SO", ct)).ShouldBe($"SO-{year}-00001");
        (await numbers.NextAsync("TR", ct)).ShouldBe($"TR-{year}-00001");
    }

    [Fact]
    public async Task DocumentNumbers_restartAfterAReset()
    {
        var ct = TestContext.Current.CancellationToken;
        // Tests assert on "-00001"; a sequence that survives ResetDatabaseAsync would make
        // every such assertion depend on test order.
        await factory.ResetDatabaseAsync();
        using (var scope = factory.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>().NextAsync("SO", ct);

        await factory.ResetDatabaseAsync();
        using var second = factory.CreateScope();
        var number = await second.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>().NextAsync("SO", ct);

        number.ShouldEndWith("-00001");
    }

    [Fact]
    public async Task DocumentNumbers_refuseAnUnknownPrefix()
    {
        var ct = TestContext.Current.CancellationToken;
        using var scope = factory.CreateScope();
        var numbers = scope.ServiceProvider.GetRequiredService<IDocumentNumberGenerator>();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => numbers.NextAsync("XX", ct));
    }

    [Fact]
    public async Task SalesOrder_roundTripsWithItsLines_andARowVersion()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.ResetDatabaseAsync();
        var warehouseId = await factory.SeedWarehouseAsync("PER-WH");
        var productA = await factory.SeedProductAsync("PER-A");
        var productB = await factory.SeedProductAsync("PER-B");
        var customerId = await factory.SeedCustomerAsync("Persist SA");

        var order = SalesOrder.Create(
            "SO-TEST-1", customerId, warehouseId, DateTimeOffset.UtcNow,
            [(productA, 5, 1000), (productB, 3, 2500)]).Value;

        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SalesOrders.Add(order);
            await db.SaveChangesAsync(ct);
        }

        using var read = factory.CreateScope();
        var loaded = await read.ServiceProvider.GetRequiredService<AppDbContext>()
            .SalesOrders.Include(o => o.Lines).SingleAsync(o => o.Id == order.Id, ct);

        loaded.Lines.Count.ShouldBe(2);
        loaded.Lines.Select(l => l.QuantityOrdered).OrderBy(q => q).ShouldBe([3, 5]);
        loaded.Lines.ShouldAllBe(l => l.SalesOrderId == order.Id);
        loaded.RowVersion.ShouldNotBe(0u);
    }

    [Fact]
    public async Task PurchaseOrderAndTransfer_roundTripWithTheirLines()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.ResetDatabaseAsync();
        var from = await factory.SeedWarehouseAsync("PER-FROM");
        var to = await factory.SeedWarehouseAsync("PER-TO");
        var transit = await factory.SeedWarehouseAsync("PER-TRN", Domain.Enums.WarehouseKind.InTransit);
        var productId = await factory.SeedProductAsync("PER-C");
        var supplierId = await factory.SeedSupplierAsync("PER-SUP");

        var po = PurchaseOrder.Create(
            "PO-TEST-1", supplierId, to, null, DateTimeOffset.UtcNow, [(productId, 10, 500)]).Value;
        var transfer = StockTransfer.Create(
            "TR-TEST-1", from, to, transit, DateTimeOffset.UtcNow, [(productId, 4)]).Value;

        using (var scope = factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PurchaseOrders.Add(po);
            db.StockTransfers.Add(transfer);
            await db.SaveChangesAsync(ct);
        }

        using var read = factory.CreateScope();
        var readDb = read.ServiceProvider.GetRequiredService<AppDbContext>();

        (await readDb.PurchaseOrders.Include(o => o.Lines).SingleAsync(o => o.Id == po.Id, ct))
            .Lines.Single().UnitCostCents.ShouldBe(500);
        (await readDb.StockTransfers.Include(t => t.Lines).SingleAsync(t => t.Id == transfer.Id, ct))
            .Lines.Single().Quantity.ShouldBe(4);
    }
}
