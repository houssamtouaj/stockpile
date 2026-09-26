using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Transfers.Queries.GetTransfer;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.Transfers.Commands.CancelTransfer;

public sealed record CancelTransferCommand(Guid TransferId) : ICommand<StockTransferDto>;

/// <summary>
/// Draft only, so there is never stock to move back. After dispatch the aggregate refuses
/// with guidance: receive at the destination, then transfer back.
/// </summary>
public sealed class CancelTransferHandler(
    IAppDbContext db,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<CancelTransferCommand, Result<StockTransferDto>>
{
    public async Task<Result<StockTransferDto>> Handle(
        CancelTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == command.TransferId, cancellationToken);

        if (transfer is null)
            return new NotFoundError("StockTransfer", command.TransferId);

        var cancelled = transfer.Cancel(clock.UtcNow);
        if (cancelled.IsFailure)
            return cancelled.Error;

        notifications.Enqueue(new OrderStatusChangedNotification(
            transfer.FromWarehouseId, transfer.Id, nameof(StockTransfer), transfer.Status.ToString()));

        return StockTransferDto.From(transfer);
    }
}
