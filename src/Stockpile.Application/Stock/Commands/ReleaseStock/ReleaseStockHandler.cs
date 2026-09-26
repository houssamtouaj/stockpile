using MediatR;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Stock;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Stock.Commands.ReleaseStock;

public sealed class ReleaseStockHandler(IAppDbContext db, IStockMutator mutator)
    : IRequestHandler<ReleaseStockCommand, Result<StockMutationOutcome>>
{
    public async Task<Result<StockMutationOutcome>> Handle(
        ReleaseStockCommand command, CancellationToken cancellationToken)
    {
        if (await PhysicalWarehouseGuard.RefuseIfNotPhysicalAsync(db, command.WarehouseId, cancellationToken)
            is { } refused)
            return refused;

        return await mutator.ApplyAsync(
            new StockMutationRequest(
                command.ProductId,
                command.WarehouseId,
                command.IdempotencyKey,
                command.ReferenceType ?? "Manual",
                command.ReferenceId,
                Reason: null,
                Operation: $"release:{command.Quantity}"),
            write: (writer, ct) =>
                writer.TryReleaseAsync(command.ProductId, command.WarehouseId, command.Quantity, ct),
            buildMovement: (context, write) =>
                StockMovement.ReservationRelease(
                    context, command.Quantity, write.OnHandAfter, write.ReservedAfter),
            // Releasing more than is reserved is a domain-rule refusal, not insufficient
            // stock. The error code differs so the Angular toast can say something true.
            onRefused: _ => new DomainRuleError(
                "stock.release_exceeds_reserved",
                $"Cannot release {command.Quantity} unit(s); fewer are currently reserved."),
            cancellationToken);
    }
}
