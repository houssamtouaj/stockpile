using MediatR;
using Stockpile.Application.Common.Exceptions;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

// MediatR 13 ships its own INotificationPublisher for its notification pipeline, which
// is unrelated to ours. Alias rather than rename: this type is named in 00-overview.md's
// shared type index and every phase from 02 onward consumes that exact name.
using INotificationPublisher = Stockpile.Application.Common.Interfaces.INotificationPublisher;

namespace Stockpile.Application.Common.Behaviors;

/// <summary>
/// Wraps commands — never queries — in a database transaction, then dispatches queued
/// real-time notifications ONLY after that transaction has committed.
/// <para>
/// Flushing inside the transaction would broadcast state to every connected client before
/// the write is durable; a rollback then leaves every browser showing a quantity that never
/// existed (§7). This ordering is asserted by TransactionBehaviorTests.
/// </para>
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    INotificationPublisher notifications)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (!IsCommand(request))
            return await next(cancellationToken);

        TResponse response;
        try
        {
            try
            {
                response = await unitOfWork.ExecuteInTransactionAsync(
                    async ct => await next(ct), cancellationToken);
            }
            catch (IdempotencyReplayException)
            {
                // The transaction rolled back, so anything the losing attempt queued
                // describes state that was never committed. Dropping it is the same rule
                // the flush-after-commit ordering exists to enforce; without it, every
                // loser of an idempotency race would broadcast a phantom quantity once
                // phase 04 puts a real transport behind FlushAsync.
                notifications.Clear();

                // Replayed exactly once. The winner is committed by now, so the handler's
                // idempotency fast path finds its movement and returns the recorded
                // after-values. A second IdempotencyReplayException would mean the fast
                // path cannot see a row the unique index says exists — a real fault, and
                // it is left to propagate as one.
                response = await unitOfWork.ExecuteInTransactionAsync(
                    async ct => await next(ct), cancellationToken);
            }
        }
        catch
        {
            notifications.Clear();
            throw;
        }

        if (response is Result { IsFailure: true })
        {
            notifications.Clear();
            return response;
        }

        await notifications.FlushAsync(cancellationToken);
        return response;
    }

    /// <summary>
    /// Walks the interface list rather than pattern-matching, because ICommand&lt;T&gt; is an
    /// open generic and `request is ICommand&lt;object&gt;` would only match one closed
    /// construction.
    /// </summary>
    private static bool IsCommand(TRequest request) =>
        request.GetType().GetInterfaces().Any(i =>
            i == typeof(ICommand) ||
            (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)));
}
