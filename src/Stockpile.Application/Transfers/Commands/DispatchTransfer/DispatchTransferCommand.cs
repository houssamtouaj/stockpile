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

namespace Stockpile.Application.Transfers.Commands.DispatchTransfer;

public sealed record DispatchTransferCommand(Guid TransferId, string IdempotencyKey)
    : ICommand<StockTransferDto>;

public sealed class DispatchTransferValidator : AbstractValidator<DispatchTransferCommand>
{
    public DispatchTransferValidator()
    {
        RuleFor(x => x.TransferId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(DerivedIdempotencyKey.MaxClientKeyLength);
    }
}

/// <summary>Source → in-transit warehouse. The units leave the source and still exist.</summary>
public sealed class DispatchTransferHandler(
    IAppDbContext db,
    IStockWriter writer,
    IStockMutator mutator,
    IClock clock,
    INotificationPublisher notifications) : IRequestHandler<DispatchTransferCommand, Result<StockTransferDto>>
{
    public async Task<Result<StockTransferDto>> Handle(
        DispatchTransferCommand command, CancellationToken cancellationToken)
    {
        var transfer = await db.StockTransfers
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.Id == command.TransferId, cancellationToken);

        if (transfer is null)
            return new NotFoundError("StockTransfer", command.TransferId);

        var transition = transfer.Dispatch(clock.UtcNow);
        if (transition.IsFailure)
            return transition.Error;

        var moved = await TransferLegs.MoveAsync(
            db, writer, mutator, transfer,
            stage: TransferStage.Dispatch,
            fromWarehouseId: transfer.FromWarehouseId,
            toWarehouseId: transfer.InTransitWarehouseId,
            command.IdempotencyKey,
            cancellationToken);

        if (moved.IsFailure)
            return moved.Error;

        // Both boards change: the source's stock left, and the destination now has inbound
        // stock to expect. The throttle coalesces by key, so the second message is free.
        var status = transfer.Status.ToString();
        notifications.Enqueue(new OrderStatusChangedNotification(
            transfer.FromWarehouseId, transfer.Id, nameof(StockTransfer), status));
        notifications.Enqueue(new OrderStatusChangedNotification(
            transfer.ToWarehouseId, transfer.Id, nameof(StockTransfer), status));

        return StockTransferDto.From(transfer);
    }
}
