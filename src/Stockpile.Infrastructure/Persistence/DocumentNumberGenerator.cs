using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;

namespace Stockpile.Infrastructure.Persistence;

/// <summary>
/// Human-readable document numbers from a Postgres sequence. A sequence rather than
/// MAX(number) + 1 because the latter races under concurrent creation and would produce
/// duplicate numbers on a unique index — a 500 on a perfectly valid request.
/// <para>
/// nextval() is non-transactional, so a rolled-back order burns a number. That is the
/// correct trade: gaps in a document sequence are normal and auditable; duplicates are not.
/// </para>
/// </summary>
internal sealed class DocumentNumberGenerator(AppDbContext db, IClock clock) : IDocumentNumberGenerator
{
    public async Task<string> NextAsync(string prefix, CancellationToken ct = default)
    {
        var sequence = prefix.ToLowerInvariant() switch
        {
            "po" => "purchase_order_number_seq",
            "so" => "sales_order_number_seq",
            "tr" => "stock_transfer_number_seq",
            _ => throw new ArgumentOutOfRangeException(nameof(prefix), prefix, "Unknown document prefix.")
        };

        // The name comes from the closed switch above, and is still sent as a parameter
        // cast to regclass rather than spliced into the SQL text: nothing to inject through,
        // and nothing for a later edit to the switch to open up.
        var next = await db.Database
            .SqlQuery<long>($"SELECT nextval({sequence}::regclass) AS \"Value\"")
            .SingleAsync(ct);

        return $"{prefix.ToUpperInvariant()}-{clock.UtcNow.Year}-{next:D5}";
    }
}
