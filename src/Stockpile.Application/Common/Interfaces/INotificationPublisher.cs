namespace Stockpile.Application.Common.Interfaces;

public interface IRealtimeNotification;

public sealed record StockChangedNotification(
    Guid WarehouseId, Guid ProductId, int OnHandAfter, int ReservedAfter) : IRealtimeNotification;

public sealed record OrderStatusChangedNotification(
    Guid WarehouseId, Guid OrderId, string OrderType, string Status) : IRealtimeNotification;

public sealed record LowStockNotification(
    Guid WarehouseId, Guid ProductId, int Available, int ReorderPoint) : IRealtimeNotification;

/// <summary>
/// Handlers enqueue; the transaction behavior flushes after commit. Handlers must never
/// reach a transport directly — that is both a dependency-rule violation and the cause of
/// the pre-commit broadcast desync described in §7.
/// </summary>
public interface INotificationPublisher
{
    void Enqueue(IRealtimeNotification notification);

    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>Drops anything queued. Called when the transaction did not commit.</summary>
    void Clear();
}
