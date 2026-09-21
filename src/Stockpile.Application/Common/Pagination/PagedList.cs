namespace Stockpile.Application.Common.Pagination;

/// <summary>
/// Offset pagination, which §8 permits everywhere except the movement ledger. The ledger
/// grows fastest and gets a keyset cursor instead — see <see cref="MovementCursor"/>.
/// </summary>
public sealed record PagedList<T>(
    IReadOnlyList<T> Items, int Page, int Size, int TotalCount)
{
    public int TotalPages => Size <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)Size);
    public bool HasMore => Page * Size < TotalCount;
}
