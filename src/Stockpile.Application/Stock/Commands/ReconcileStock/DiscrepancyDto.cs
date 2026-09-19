namespace Stockpile.Application.Stock.Commands.ReconcileStock;

public sealed record DiscrepancyDto(
    Guid ProductId,
    string Sku,
    Guid WarehouseId,
    string WarehouseCode,
    int SnapshotOnHand,
    int LedgerOnHand,
    int SnapshotReserved,
    int LedgerReserved);

public sealed record ReconciliationReport(
    int RowsChecked,
    int DiscrepancyCount,
    IReadOnlyList<DiscrepancyDto> Discrepancies,
    bool Repaired);
