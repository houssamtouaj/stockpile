namespace Stockpile.Application.Common.Stock;

/// <summary>
/// One stable key per (request, leg, line), derived from the key the client sent. An order
/// command writes a movement per line; a single shared key would let the mutator's replay
/// check short-circuit after the first line, so a retry would report success while leaving
/// the remaining lines untouched.
/// </summary>
public static class DerivedIdempotencyKey
{
    /// <summary>stock_movements.idempotency_key holds 100 characters.</summary>
    public const int MaxKeyLength = 100;

    /// <summary>
    /// The cap on every client key an order or transfer endpoint accepts, whether it is
    /// derived from or used as-is. The longest leg below is "dispatch-out", so the longest
    /// suffix is ":dispatch-out:" plus a 36-character Guid — 50 characters — and a 50-character
    /// client key derives exactly 100. DerivedIdempotencyKeyTests holds every leg to that.
    /// </summary>
    public const int MaxClientKeyLength = 50;

    public const string ConfirmLine = "line";
    public const string ShipLine = "issue";
    public const string CancelLine = "release";
    public const string DispatchOut = "dispatch-out";
    public const string DispatchIn = "dispatch-in";
    public const string ReceiveOut = "receive-out";
    public const string ReceiveIn = "receive-in";

    public static IReadOnlyList<string> Legs { get; } =
        [ConfirmLine, ShipLine, CancelLine, DispatchOut, DispatchIn, ReceiveOut, ReceiveIn];

    /// <summary>
    /// Only the listed legs: an unlisted one would escape the length test, and the first
    /// sign of a leg too long for the column would be a 500 in production.
    /// </summary>
    public static string For(string clientKey, string leg, Guid lineId) =>
        Legs.Contains(leg)
            ? $"{clientKey}:{leg}:{lineId}"
            : throw new ArgumentOutOfRangeException(nameof(leg), leg, "Add the leg to DerivedIdempotencyKey.Legs.");
}
