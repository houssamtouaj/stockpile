using Microsoft.Extensions.Logging;
using Stockpile.Application.Common.Interfaces;

namespace Stockpile.Infrastructure.Realtime;

/// <summary>
/// Scoped per request. Phase 04 replaces the FlushAsync body with a SignalR dispatch;
/// the queue-and-flush-after-commit contract is established here so handlers written in
/// phases 02 and 03 never need to change.
/// </summary>
public sealed class NotificationQueue(ILogger<NotificationQueue> logger) : INotificationPublisher
{
    private readonly List<IRealtimeNotification> _pending = [];

    public void Enqueue(IRealtimeNotification notification) => _pending.Add(notification);

    public void Clear() => _pending.Clear();

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        foreach (var notification in _pending)
            logger.LogDebug("Post-commit notification (no transport yet): {Notification}", notification);

        _pending.Clear();
        return Task.CompletedTask;
    }
}
