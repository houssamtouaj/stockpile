using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Common.Stock;

public sealed record StockMutationRequest(
    Guid ProductId,
    Guid WarehouseId,
    string IdempotencyKey,
    string ReferenceType,
    Guid? ReferenceId,
    string? Reason,
    string Operation);

public sealed record StockMutationOutcome(int OnHandAfter, int ReservedAfter, bool WasReplay);

/// <summary>
/// What the row actually holds at the moment a write was refused, handed to whichever
/// message the caller wants to build.
/// <para>
/// Both numbers, not just availability: a reservation refusal has to quote what is
/// AVAILABLE, while a cycle-count refusal has to quote what is RESERVED — "release some of
/// the 8 units you are holding" is the only instruction an operator can act on. Passing one
/// number and letting each message label it is what had the count refusal report the
/// availability figure as though it were the reserved quantity.
/// </para>
/// </summary>
public sealed record StockOnRefusal(int OnHand, int Reserved)
{
    public int Available => OnHand - Reserved;
}

public interface IStockMutator
{
    Task<Result<StockMutationOutcome>> ApplyAsync(
        StockMutationRequest request,
        Func<IStockWriter, CancellationToken, Task<StockWriteResult>> write,
        Func<MovementContext, StockWriteResult, StockMovement> buildMovement,
        Func<StockOnRefusal, Error> onRefused,
        CancellationToken cancellationToken);
}

/// <summary>
/// The one sequence every stock mutation follows:
///
///   1. Replay check — a movement already carrying this key means this request has
///      already been applied. Return its recorded after-values, not fresh ones.
///   2. The atomic conditional UPDATE.
///   3. Map a refusal to the caller's chosen error (422).
///   4. Append the ledger row, with OnHandAfter/ReservedAfter taken from RETURNING.
///   5. Enqueue a post-commit notification. Never send one here.
///
/// Centralising it is what stops step 4 from quietly regressing into "read the entity and
/// use its current values", which is the mistake §6 spends a page warning about.
/// </summary>
public sealed class StockMutator(
    IAppDbContext db,
    IStockWriter writer,
    IClock clock,
    ICurrentUser currentUser,
    INotificationPublisher notifications) : IStockMutator
{
    public async Task<Result<StockMutationOutcome>> ApplyAsync(
        StockMutationRequest request,
        Func<IStockWriter, CancellationToken, Task<StockWriteResult>> write,
        Func<MovementContext, StockWriteResult, StockMovement> buildMovement,
        Func<StockOnRefusal, Error> onRefused,
        CancellationToken cancellationToken)
    {
        // 1. Fast-path replay check. The unique index on idempotency_key is the real
        //    guarantee; this just avoids doing the work twice in the common case.
        var requestHash = HashOf(request);

        var existing = await db.StockMovements
            .AsNoTracking()
            .Where(m => m.IdempotencyKey == request.IdempotencyKey)
            .Select(m => new { m.OnHandAfter, m.ReservedAfter, m.RequestHash })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            // The key alone says "you have seen this attempt before", not "this is the
            // same request". A client that edits an order line and retries under the key
            // it already used gets the FIRST request's numbers back, its second mutation
            // is silently dropped, and nothing anywhere records that it happened. Refuse
            // instead. A row with no stored digest predates the column and cannot be
            // judged, so it keeps the old behaviour rather than failing a valid retry.
            if (existing.RequestHash is not null && existing.RequestHash != requestHash)
                return new IdempotencyKeyReusedError(request.IdempotencyKey);

            return new StockMutationOutcome(existing.OnHandAfter, existing.ReservedAfter, WasReplay: true);
        }

        // 2. The atomic conditional UPDATE.
        var result = await write(writer, cancellationToken);

        // 3. Refusals. RowMissing and InsufficientStock are different answers and get
        //    different status codes: there is no stock row for this (product, warehouse)
        //    pair at all — 404 — versus the row exists and does not hold enough — 422.
        //    Collapsing them into one 422 would tell a client to wait for stock that is
        //    never coming, and would waste the `target` CTE that exists to tell them apart.
        if (result.IsRowMissing)
            return new StockItemNotFoundError(request.ProductId, request.WarehouseId);

        if (result.IsInsufficient)
        {
            var held = await db.StockItems
                .AsNoTracking()
                .Where(s => s.ProductId == request.ProductId && s.WarehouseId == request.WarehouseId)
                .Select(s => new StockOnRefusal(s.QuantityOnHand, s.QuantityReserved))
                .FirstOrDefaultAsync(cancellationToken);

            return onRefused(held ?? new StockOnRefusal(0, 0));
        }

        // 4. The ledger row. After-values come from RETURNING, never from a re-read.
        var context = new MovementContext(
            ProductId: request.ProductId,
            WarehouseId: request.WarehouseId,
            ReferenceType: request.ReferenceType,
            ReferenceId: request.ReferenceId,
            IdempotencyKey: request.IdempotencyKey,
            Reason: request.Reason,
            OccurredAt: clock.UtcNow,
            PerformedByUserId: currentUser.UserId ?? Guid.Empty,
            RequestHash: requestHash);

        var movement = buildMovement(context, result);

        // A movement that moves nothing would breach movement_deltas_not_both_zero. The
        // common case is a cycle count that matches the system exactly — a real and
        // frequent outcome that must be refused cleanly with a 422 rather than blowing up
        // on the constraint with a 500. Guarding here covers every current and future
        // mutation type and keeps the CHECK constraint a genuine backstop.
        if (movement.OnHandDelta == 0 && movement.ReservedDelta == 0)
            return new DomainRuleError(
                "stock.count_unchanged",
                "The counted quantity matches the recorded quantity; nothing to record.");

        db.StockMovements.Add(movement);

        // 5. Queued. TransactionBehavior dispatches it after the commit.
        notifications.Enqueue(new StockChangedNotification(
            request.WarehouseId, request.ProductId, result.OnHandAfter, result.ReservedAfter));

        return new StockMutationOutcome(result.OnHandAfter, result.ReservedAfter, WasReplay: false);
    }

    /// <summary>
    /// What the key stood for: the pairing being moved and the operation moving it. Hashed
    /// rather than stored verbatim so the column stays a fixed 64 characters no matter what
    /// a future mutation type wants to put in Operation.
    /// </summary>
    private static string HashOf(StockMutationRequest request) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{request.ProductId}|{request.WarehouseId}|{request.Operation}")));
}
