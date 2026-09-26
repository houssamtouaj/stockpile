using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Stock;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Transfers.Commands;

/// <summary>
/// Moves every line of a transfer from one warehouse to another as a paired TransferOut /
/// TransferIn, through the same mutation path every other stock change takes — no new SQL.
/// Dispatch calls it From → InTransit; receipt calls it InTransit → To.
/// <para>
/// Both legs of every line run inside the handler's one transaction, so a transfer can
/// never half-happen. That is the mechanical reason total valuation is conserved
/// mid-flight: the units are always in exactly one stock row, at one cost.
/// </para>
/// </summary>
internal static class TransferLegs
{
    public static async Task<Result> MoveAsync(
        IAppDbContext db,
        IStockWriter writer,
        IStockMutator mutator,
        StockTransfer transfer,
        string stage,
        Guid fromWarehouseId,
        Guid toWarehouseId,
        string idempotencyKey,
        CancellationToken ct)
    {
        foreach (var line in transfer.Lines)
        {
            // The cost the units carry as they move: read from the source BEFORE the
            // outbound write, and used on the inbound leg, which is what conserves total
            // valuation. The outbound leg leaves the source's average untouched, so the
            // value removed there is exactly the value added here — up to the rounding of
            // a blended average when the destination already holds the product at another
            // cost. A concurrent receipt at the source can make this figure slightly stale;
            // both are accepted limitations of integer-cent weighted-average costing.
            var unitCost = await db.StockItems
                .AsNoTracking()
                .Where(s => s.ProductId == line.ProductId && s.WarehouseId == fromWarehouseId)
                .Select(s => (long?)s.AverageUnitCostCents)
                .FirstOrDefaultAsync(ct) ?? 0;

            var outbound = await mutator.ApplyAsync(
                new StockMutationRequest(
                    line.ProductId, fromWarehouseId,
                    DerivedIdempotencyKey.For(idempotencyKey, $"{stage}-out", line.Id),
                    nameof(StockTransfer), transfer.Id, Reason: null,
                    Operation: $"tr-{stage}:out:{line.Quantity}"),
                write: (w, c) => w.TryAdjustAsync(line.ProductId, fromWarehouseId, -line.Quantity, c),
                buildMovement: (context, write) => StockMovement.TransferOut(
                    context, line.Quantity, write.OnHandAfter, write.ReservedAfter),
                onRefused: held => new InsufficientStockError(line.Quantity, held.Available),
                ct);

            if (outbound.IsFailure)
                return outbound.Error;

            await writer.EnsureStockItemAsync(line.ProductId, toWarehouseId, ct);

            var inbound = await mutator.ApplyAsync(
                new StockMutationRequest(
                    line.ProductId, toWarehouseId,
                    DerivedIdempotencyKey.For(idempotencyKey, $"{stage}-in", line.Id),
                    nameof(StockTransfer), transfer.Id, Reason: null,
                    Operation: $"tr-{stage}:in:{line.Quantity}"),
                write: (w, c) => w.TryReceiveAsync(line.ProductId, toWarehouseId, line.Quantity, unitCost, c),
                buildMovement: (context, write) => StockMovement.TransferIn(
                    context, line.Quantity, unitCost, write.OnHandAfter, write.ReservedAfter),
                onRefused: _ => new DomainRuleError(
                    "transfer.inbound_failed", "The inbound leg of the transfer could not be applied."),
                ct);

            if (inbound.IsFailure)
                return inbound.Error;
        }

        return Result.Ok();
    }
}
