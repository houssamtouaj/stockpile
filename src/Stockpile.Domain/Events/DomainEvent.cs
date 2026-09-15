namespace Stockpile.Domain.Events;

public abstract record DomainEvent(DateTimeOffset OccurredAt);

public sealed record StockChangedEvent(
    Guid ProductId,
    Guid WarehouseId,
    int OnHandAfter,
    int ReservedAfter,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record StockDepletedEvent(
    Guid ProductId,
    Guid WarehouseId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record LowStockDetectedEvent(
    Guid ProductId,
    Guid WarehouseId,
    int Available,
    int ReorderPoint,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

public sealed record OrderStatusChangedEvent(
    Guid OrderId,
    string OrderType,
    string FromStatus,
    string ToStatus,
    Guid WarehouseId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
