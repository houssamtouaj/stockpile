namespace Stockpile.Application.Common.Interfaces;

public enum StockWriteOutcome { Applied, InsufficientStock, RowMissing }

public sealed record StockWriteResult(
    StockWriteOutcome Outcome, int OnHandAfter, int ReservedAfter, int PreviousOnHand)
{
    public bool IsApplied      => Outcome is StockWriteOutcome.Applied;
    public bool IsInsufficient => Outcome is StockWriteOutcome.InsufficientStock;
    public bool IsRowMissing   => Outcome is StockWriteOutcome.RowMissing;
}

/// <summary>
/// The ONLY sanctioned way to mutate stock_items. Each method is a single atomic
/// conditional UPDATE: the availability check and the write happen in one statement, so
/// there is no read-then-write window to lose (§6).
/// <para>
/// Zero rows updated means the precondition was false — insufficient stock. That is a
/// correct refusal (422), not a conflict (409) and not something to retry.
/// </para>
/// <para>
/// Every implementation MUST run on the DbContext's connection and ambient transaction,
/// so the snapshot write and the ledger append commit or roll back together.
/// </para>
/// </summary>
public interface IStockWriter
{
    Task EnsureStockItemAsync(Guid productId, Guid warehouseId, CancellationToken ct = default);

    /// <summary>
    /// Row-locks every existing stock row in <paramref name="rows"/> in one statement, in id
    /// order. A command that writes more than one row calls this before its first write, so
    /// every such command acquires its locks in the same global order and no two of them can
    /// each hold a row the other is waiting for. Walking lines in load order is what let two
    /// confirms with lines [A,B] and [B,A] — or a dispatch W1 → W2 and a receipt W2 → W1,
    /// which share the in-transit row — deadlock.
    /// <para>
    /// A pairing with no row is skipped rather than created: the write that follows reports
    /// it as missing, exactly as it would have without the lock.
    /// </para>
    /// </summary>
    Task LockRowsAsync(IReadOnlyCollection<(Guid ProductId, Guid WarehouseId)> rows, CancellationToken ct = default);

    Task<StockWriteResult> TryReserveAsync(Guid productId, Guid warehouseId, int quantity, CancellationToken ct = default);
    Task<StockWriteResult> TryReleaseAsync(Guid productId, Guid warehouseId, int quantity, CancellationToken ct = default);
    Task<StockWriteResult> TryIssueAsync(Guid productId, Guid warehouseId, int quantity, CancellationToken ct = default);
    Task<StockWriteResult> TryReceiveAsync(Guid productId, Guid warehouseId, int quantity, long unitCostCents, CancellationToken ct = default);
    Task<StockWriteResult> TryAdjustAsync(Guid productId, Guid warehouseId, int onHandDelta, CancellationToken ct = default);
    Task<StockWriteResult> TryCountAsync(Guid productId, Guid warehouseId, int observedOnHand, DateTimeOffset countedAt, CancellationToken ct = default);
}
