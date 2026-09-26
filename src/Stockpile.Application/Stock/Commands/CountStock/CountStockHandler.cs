using MediatR;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Stock;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Stock.Commands.CountStock;

/// <summary>
/// A count whose observed quantity matches the recorded one is a real and common outcome.
/// It is refused with 422 stock.count_unchanged by the zero-delta guard inside
/// <see cref="StockMutator"/> rather than being allowed to trip the
/// movement_deltas_not_both_zero constraint with a 500.
/// <para>
/// The write stamps last_counted_at before that refusal is discovered, and the stamp is
/// undone with the rest of the transaction — which is the correct outcome, and only true
/// because the writer shares the ambient transaction.
/// </para>
/// </summary>
public sealed class CountStockHandler(IAppDbContext db, IStockMutator mutator, IClock clock)
    : IRequestHandler<CountStockCommand, Result<StockMutationOutcome>>
{
    public async Task<Result<StockMutationOutcome>> Handle(
        CountStockCommand command, CancellationToken cancellationToken)
    {
        if (await PhysicalWarehouseGuard.RefuseIfNotPhysicalAsync(db, command.WarehouseId, cancellationToken)
            is { } refused)
            return refused;

        return await mutator.ApplyAsync(
            new StockMutationRequest(
                command.ProductId, command.WarehouseId, command.IdempotencyKey,
                ReferenceType: "CycleCount", ReferenceId: null, Reason: command.Reason,
                Operation: $"count:{command.ObservedOnHand}"),
            write: (writer, ct) => writer.TryCountAsync(
                command.ProductId, command.WarehouseId, command.ObservedOnHand, clock.UtcNow, ct),
            buildMovement: (context, write) =>
                StockMovement.Count(
                    context, write.PreviousOnHand, command.ObservedOnHand, write.ReservedAfter),
            onRefused: held => new DomainRuleError(
                "stock.count_refused",
                $"A count of {command.ObservedOnHand} is below the {held.Reserved} unit(s) "
                + "currently reserved. Release the reservations first."),
            cancellationToken);
    }
}
