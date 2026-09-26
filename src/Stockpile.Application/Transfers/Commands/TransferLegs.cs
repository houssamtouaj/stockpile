using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Stock;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Transfers.Commands;

internal enum TransferStage { Dispatch, Receive }

/// <summary>
/// Moves every line of a transfer from one warehouse to another as a paired TransferOut /
/// TransferIn, through the same mutation path every other stock change takes. Dispatch
/// calls it From → InTransit; receipt calls it InTransit → To.
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
        TransferStage stage,
        Guid fromWarehouseId,
        Guid toWarehouseId,
        string idempotencyKey,
        CancellationToken ct)
    {
        // Every row either leg writes. Created first, in one fixed order so two commands
        // creating the same rows cannot wait on each other's inserts; then locked together
        // in id order before anything is written. Locking leg by leg is what let a dispatch
        // W1 → W2 and a receipt W2 → W1 deadlock on the in-transit row they share.
        var rows = transfer.Lines
            .SelectMany(l => new[] { (l.ProductId, fromWarehouseId), (l.ProductId, toWarehouseId) })
            .Order()
            .ToList();

        foreach (var (productId, warehouseId) in rows)
            await writer.EnsureStockItemAsync(productId, warehouseId, ct);

        await writer.LockRowsAsync(rows, ct);

        var stageName = stage == TransferStage.Dispatch ? "dispatch" : "receive";

        foreach (var line in transfer.Lines)
        {
            // The cost the units carry, used on BOTH legs, so the value one row gives up is
            // exactly the value the other gains. Dispatch prices them at the source's average
            // — read under the lock above, so no receipt can move it between this read and
            // the write — and records that on the line. Receipt reuses the recorded cost
            // rather than the in-transit row's average, which blends every transfer of this
            // product still on the road. A line dispatched before costs were recorded has
            // none, and falls back to that average.
            var unitCost = stage == TransferStage.Receive && line.UnitCostCents is { } recorded
                ? recorded
                : await AverageCostAsync(db, line.ProductId, fromWarehouseId, ct);

            if (stage == TransferStage.Dispatch)
            {
                var priced = transfer.RecordDispatchCost(line.Id, unitCost);
                if (priced.IsFailure)
                    return priced.Error;
            }

            var outbound = await mutator.ApplyAsync(
                new StockMutationRequest(
                    line.ProductId, fromWarehouseId,
                    DerivedIdempotencyKey.For(idempotencyKey, $"{stageName}-out", line.Id),
                    nameof(StockTransfer), transfer.Id, Reason: null,
                    Operation: $"tr-{stageName}:out:{line.Quantity}"),
                write: (w, c) => w.TryWithdrawAtCostAsync(line.ProductId, fromWarehouseId, line.Quantity, unitCost, c),
                buildMovement: (context, write) => StockMovement.TransferOut(
                    context, line.Quantity, write.OnHandAfter, write.ReservedAfter),
                onRefused: held => new InsufficientStockError(line.Quantity, held.Available),
                ct);

            if (outbound.IsFailure)
                return outbound.Error;

            var inbound = await mutator.ApplyAsync(
                new StockMutationRequest(
                    line.ProductId, toWarehouseId,
                    DerivedIdempotencyKey.For(idempotencyKey, $"{stageName}-in", line.Id),
                    nameof(StockTransfer), transfer.Id, Reason: null,
                    Operation: $"tr-{stageName}:in:{line.Quantity}"),
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

    private static async Task<long> AverageCostAsync(
        IAppDbContext db, Guid productId, Guid warehouseId, CancellationToken ct) =>
        await db.StockItems
            .AsNoTracking()
            .Where(s => s.ProductId == productId && s.WarehouseId == warehouseId)
            .Select(s => (long?)s.AverageUnitCostCents)
            .FirstOrDefaultAsync(ct) ?? 0;
}
