namespace Stockpile.Application.Common.Exceptions;

/// <summary>
/// Postgres picked this transaction as a deadlock victim (40P01) or refused to serialize it
/// (40001), and the unit of work rolled it back. Nothing it wrote survives, so running it
/// again from the top is always safe.
/// <para>
/// Like <see cref="IdempotencyReplayException"/>, a signal rather than a fault:
/// TransactionBehavior discards what the attempt queued and retries a bounded number of
/// times. Escaping the pipeline means every retry collided too, and the API answers 409 with
/// a retryable error code — never 500, because the request itself was fine.
/// </para>
/// </summary>
public sealed class TransientConflictException(string sqlState, Exception innerException)
    : Exception($"The transaction was rolled back by a transient conflict (SQLSTATE {sqlState}).", innerException)
{
    public string SqlState { get; } = sqlState;
}
