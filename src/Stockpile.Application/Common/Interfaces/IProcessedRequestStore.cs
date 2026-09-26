namespace Stockpile.Application.Common.Interfaces;

public enum ProcessedRequestMatch { New, SameRequest, DifferentRequest }

/// <summary>
/// Idempotency for a command that writes no stock movement, and so cannot lean on the
/// unique key in stock_movements the way every stock mutation does. The handler asks
/// before it acts and records after; the record is saved in the command's own
/// transaction, so it exists exactly when the effect it vouches for does.
/// <para>
/// The fingerprint says what the key stood for. A key already recorded against a
/// different fingerprint is a reuse, refused rather than answered with the first
/// request's result — the same rule the stock mutator applies.
/// </para>
/// </summary>
public interface IProcessedRequestStore
{
    Task<ProcessedRequestMatch> MatchAsync(string idempotencyKey, string fingerprint, CancellationToken ct = default);

    void Record(string idempotencyKey, string fingerprint);
}
