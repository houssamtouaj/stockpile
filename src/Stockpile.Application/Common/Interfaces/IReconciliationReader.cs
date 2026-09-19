using Stockpile.Application.Stock.Commands.ReconcileStock;

namespace Stockpile.Application.Common.Interfaces;

/// <summary>
/// Replays the ledger and compares it against the snapshot. The direction is the whole
/// point: stock_movements is the truth, stock_items is the cache, and a repair rebuilds
/// the cache — it never edits history to match a corrupted cache.
/// </summary>
public interface IReconciliationReader
{
    Task<IReadOnlyList<DiscrepancyDto>> FindDiscrepanciesAsync(CancellationToken ct = default);
    Task<int> CountStockRowsAsync(CancellationToken ct = default);
    Task<int> RepairAsync(CancellationToken ct = default);
}
