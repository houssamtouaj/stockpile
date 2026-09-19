namespace Stockpile.Application.Stock.Queries.GetStockLevels;

/// <summary>
/// AverageUnitCostCents is nullable because phase 05 projects it out entirely for a
/// Viewer or Operator — the column is never read, rather than read and then hidden.
/// </summary>
public sealed record StockLevelDto(
    Guid ProductId,
    string Sku,
    string ProductName,
    Guid WarehouseId,
    string WarehouseCode,
    int QuantityOnHand,
    int QuantityReserved,
    int QuantityAvailable,
    long? AverageUnitCostCents,
    string? BinLocation,
    int ReorderPoint,
    bool IsLowStock);
