namespace Stockpile.Application.Common.Stock;

/// <summary>
/// One stable key per (request, leg, line), derived from the key the client sent. An order
/// command writes a movement per line; a single shared key would let the mutator's replay
/// check short-circuit after the first line, so a retry would report success while leaving
/// the remaining lines untouched.
/// </summary>
public static class DerivedIdempotencyKey
{
    /// <summary>
    /// stock_movements.idempotency_key holds 100 characters. The longest suffix appended
    /// here is ":release:" plus a 36-character Guid, 45 in all, so a client key longer than
    /// 50 would overflow the column and fail as a 500 instead of a 400.
    /// </summary>
    public const int MaxClientKeyLength = 50;

    public static string For(string clientKey, string leg, Guid lineId) => $"{clientKey}:{leg}:{lineId}";
}
