namespace Stockpile.Application.Common.Exceptions;

/// <summary>
/// A concurrent request carrying the same idempotency key committed first, so this
/// transaction lost the race on the unique index and was rolled back.
/// <para>
/// This is a signal, not a fault. TransactionBehavior catches it, discards everything the
/// rolled-back attempt computed — the queued notification included — and replays the
/// operation exactly once. Only the replay can produce the winner's after-values; the
/// losing attempt's conditional UPDATE described a row state that no longer exists.
/// </para>
/// <para>
/// It must never reach a caller. Escaping the pipeline means the replay itself failed to
/// find a movement the unique index says is committed, which is a genuine 500.
/// </para>
/// </summary>
public sealed class IdempotencyReplayException(string constraintName)
    : Exception($"An idempotency key was already applied by a concurrent request (unique index '{constraintName}').")
{
    public string ConstraintName { get; } = constraintName;
}
