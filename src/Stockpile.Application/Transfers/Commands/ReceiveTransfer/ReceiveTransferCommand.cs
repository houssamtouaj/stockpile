using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.Transfers.Queries.GetTransfer;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.Transfers.Commands.ReceiveTransfer;

public sealed record ReceiveTransferCommand(Guid TransferId, string IdempotencyKey)
    : ICommand<StockTransferDto>;

public sealed class ReceiveTransferValidator : AbstractValidator<ReceiveTransferCommand>
{
    public ReceiveTransferValidator()
    {
        RuleFor(x => x.TransferId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(DerivedIdempotencyKey.MaxClientKeyLength);
    }
}

/// <summary>In-transit warehouse → destination. Receipt is all lines at once.</summary>
public sealed class ReceiveTransferHandler(
    IAppDbContext db,
    IStockWriter writer,
    IStockMutator mutator,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<ReceiveTransferCommand, Result<StockTransferDto>>
{
    public async Task<Result<StockTransferDto>> Handle(
        ReceiveTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == command.TransferId, cancellationToken);

        if (transfer is null)
            return new NotFoundError("StockTransfer", command.TransferId);

        var transition = transfer.Receive(clock.UtcNow);
        if (transition.IsFailure)
            return transition.Error;

        var moved = await TransferLegs.MoveAsync(
            db, writer, mutator, transfer,
            stage: TransferStage.Receive,
            fromWarehouseId: transfer.InTransitWarehouseId,
            toWarehouseId: transfer.ToWarehouseId,
            command.IdempotencyKey,
            cancellationToken);

        if (moved.IsFailure)
            return moved.Error;

        notifications.Enqueue(new OrderStatusChangedNotification(
            transfer.ToWarehouseId, transfer.Id, nameof(StockTransfer), transfer.Status.ToString()));

        return StockTransferDto.From(transfer);
    }
}
