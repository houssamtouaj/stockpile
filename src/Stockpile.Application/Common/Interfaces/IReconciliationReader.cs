using Stockpile.Application.Stock.Commands.ReconcileStock;

namespace Stockpile.Application.Common.Interfaces;

/// <summary>
/// Replays the ledger and compares it against the snapshot. The direction is the whole
/// point: stock_movements is the truth, stock_items is the cache, and a repair rebuilds
/// the cache — it never edits history to match a corrupted cache.
/// </summary>
public interface IReconciliationReader
{
    /// <summary>
    /// Locks the ledger and the snapshot against concurrent writers for the rest of the
    /// ambient transaction. Called before the scan when — and only when — a repair will
    /// follow.
    /// <para>
    /// Without it the repair races the thing it exists to fix. The ledger total is computed
    /// from the scan's snapshot, but the UPDATE re-evaluates rows against versions committed
    /// after it, so a reservation that commits mid-statement has its quantity_reserved
    /// overwritten by a total that does not include its movement: the repair MANUFACTURES
    /// the discrepancy it was run to remove. The same window sits between the scan and the
    /// repair, which are separate statements.
    /// </para>
    /// <para>
    /// Readers are unaffected — a plain SELECT needs only ACCESS SHARE. Stock mutations wait,
    /// which is the correct trade for an administrator action that rewrites every snapshot.
    /// </para>
    /// </summary>
    Task LockForRepairAsync(CancellationToken ct = default);

    Task<IReadOnlyList<DiscrepancyDto>> FindDiscrepanciesAsync(CancellationToken ct = default);
    Task<int> CountStockRowsAsync(CancellationToken ct = default);
    Task<int> RepairAsync(CancellationToken ct = default);
}
