using MediatR;
using Stockpile.Application.Common.Stock;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Stock.Commands.ReserveStock;

public sealed class ReserveStockHandler(IStockMutator mutator)
    : IRequestHandler<ReserveStockCommand, Result<StockMutationOutcome>>
{
    public Task<Result<StockMutationOutcome>> Handle(
        ReserveStockCommand command, CancellationToken cancellationToken) =>
        mutator.ApplyAsync(
            new StockMutationRequest(
                command.ProductId,
                command.WarehouseId,
                command.IdempotencyKey,
                command.ReferenceType ?? "Manual",
                command.ReferenceId,
                Reason: null,
                Operation: $"reserve:{command.Quantity}"),
            write: (writer, ct) =>
                writer.TryReserveAsync(command.ProductId, command.WarehouseId, command.Quantity, ct),
            buildMovement: (context, write) =>
                StockMovement.ReservationHold(
                    context, command.Quantity, write.OnHandAfter, write.ReservedAfter),
            onRefused: held =>
                new InsufficientStockError(command.Quantity, held.Available),
            cancellationToken);
}
