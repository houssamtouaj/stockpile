using System.Text;
using System.Text.Json;

namespace Stockpile.Application.Common.Pagination;

public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);

/// <summary>
/// Keyset pagination over (occurred_at DESC, id DESC).
/// <para>
/// §8 asks for cursor pagination on the movements endpoint specifically, because it grows
/// fastest. The reason is not style: OFFSET makes the database walk and discard every
/// skipped row, so page 5000 of a ledger costs 5000 pages of work, and any insert during
/// paging shifts every subsequent page. A keyset cursor is O(1) per page and stable
/// against concurrent inserts — which a live ledger has constantly.
/// </para>
/// </summary>
public sealed record MovementCursor(DateTimeOffset OccurredAt, Guid Id)
{
    public string Encode() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)));

    public static MovementCursor? TryDecode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            return JsonSerializer.Deserialize<MovementCursor>(
                Encoding.UTF8.GetString(Convert.FromBase64String(raw)));
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            return null;   // a malformed cursor starts from the beginning, it does not 500
        }
    }
}
