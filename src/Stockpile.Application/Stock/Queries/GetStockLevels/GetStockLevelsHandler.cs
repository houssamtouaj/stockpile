using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Stock.Queries.GetStockLevels;

public sealed class GetStockLevelsHandler(IAppDbContext db)
    : IRequestHandler<GetStockLevelsQuery, Result<IReadOnlyList<StockLevelDto>>>
{
    public async Task<Result<IReadOnlyList<StockLevelDto>>> Handle(
        GetStockLevelsQuery query, CancellationToken cancellationToken)
    {
        var source =
            from stock in db.StockItems.AsNoTracking()
            join product in db.Products.AsNoTracking() on stock.ProductId equals product.Id
            join warehouse in db.Warehouses.AsNoTracking() on stock.WarehouseId equals warehouse.Id
            select new { stock, product, warehouse };

        if (query.ProductId is { } productId)
            source = source.Where(x => x.stock.ProductId == productId);

        if (query.WarehouseId is { } warehouseId)
            source = source.Where(x => x.stock.WarehouseId == warehouseId);

        // Applied as a Where on the same expression the DTO projects, so the filter runs
        // in SQL rather than over a materialised list.
        if (query.LowStockOnly)
        {
            source = source.Where(x =>
                x.stock.QuantityOnHand - x.stock.QuantityReserved <= x.product.ReorderPoint);
        }

        // Sku is value-converted, so `product.Sku.Value` cannot be translated. Selecting
        // the property itself is still a single column in the SELECT list; it is unwrapped
        // once the row is in memory.
        var rows = await source
            .OrderBy(x => x.warehouse.Code)
            .Select(x => new
            {
                x.stock.ProductId,
                x.product.Sku,
                ProductName = x.product.Name,
                x.stock.WarehouseId,
                WarehouseCode = x.warehouse.Code,
                x.stock.QuantityOnHand,
                x.stock.QuantityReserved,
                QuantityAvailable = x.stock.QuantityOnHand - x.stock.QuantityReserved,
                x.stock.AverageUnitCostCents,
                x.stock.BinLocation,
                x.product.ReorderPoint,
                IsLowStock = x.stock.QuantityOnHand - x.stock.QuantityReserved <= x.product.ReorderPoint
            })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new StockLevelDto(
            r.ProductId, r.Sku.Value, r.ProductName,
            r.WarehouseId, r.WarehouseCode,
            r.QuantityOnHand, r.QuantityReserved, r.QuantityAvailable,
            r.AverageUnitCostCents, r.BinLocation, r.ReorderPoint, r.IsLowStock)).ToList();
    }
}
