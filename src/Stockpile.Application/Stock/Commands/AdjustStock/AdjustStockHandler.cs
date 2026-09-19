using MediatR;
using Stockpile.Application.Common.Stock;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Stock.Commands.AdjustStock;

public sealed class AdjustStockHandler(IStockMutator mutator)
    : IRequestHandler<AdjustStockCommand, Result<StockMutationOutcome>>
{
    public Task<Result<StockMutationOutcome>> Handle(
        AdjustStockCommand command, CancellationToken cancellationToken) =>
        mutator.ApplyAsync(
            new StockMutationRequest(
                command.ProductId, command.WarehouseId, command.IdempotencyKey,
                ReferenceType: "Adjustment", ReferenceId: null, Reason: command.Reason),
            write: (writer, ct) =>
                writer.TryAdjustAsync(command.ProductId, command.WarehouseId, command.OnHandDelta, ct),
            buildMovement: (context, write) =>
                StockMovement.Adjustment(
                    context, command.OnHandDelta, write.OnHandAfter, write.ReservedAfter),
            onRefused: available => new DomainRuleError(
                "stock.adjustment_refused",
                $"An adjustment of {command.OnHandDelta} would take on-hand below zero or "
                + $"below the reserved quantity. {available} unit(s) are currently available."),
            cancellationToken);
}
