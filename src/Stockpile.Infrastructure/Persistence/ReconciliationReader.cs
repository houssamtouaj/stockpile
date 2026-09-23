using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Stock.Commands.ReconcileStock;

namespace Stockpile.Infrastructure.Persistence;

internal sealed class ReconciliationReader(AppDbContext db) : IReconciliationReader
{
    /// <summary>
    /// FULL OUTER JOIN, not LEFT JOIN: a ledger group with no snapshot row is just as much
    /// a discrepancy as a snapshot row the ledger cannot explain, and a left join would
    /// silently miss the first case.
    /// </summary>
    private const string FindSql = """
        WITH ledger AS (
            SELECT product_id,
                   warehouse_id,
                   SUM(on_hand_delta)  AS on_hand,
                   SUM(reserved_delta) AS reserved
            FROM stock_movements
            GROUP BY product_id, warehouse_id
        )
        SELECT
            COALESCE(s.product_id,   l.product_id)   AS product_id,
            COALESCE(p.sku, '<orphan ledger>')       AS sku,
            COALESCE(s.warehouse_id, l.warehouse_id) AS warehouse_id,
            COALESCE(w.code, '<orphan ledger>')      AS warehouse_code,
            COALESCE(s.quantity_on_hand, 0)          AS snapshot_on_hand,
            COALESCE(l.on_hand, 0)::int              AS ledger_on_hand,
            COALESCE(s.quantity_reserved, 0)         AS snapshot_reserved,
            COALESCE(l.reserved, 0)::int             AS ledger_reserved
        FROM stock_items s
        FULL OUTER JOIN ledger l
            ON l.product_id = s.product_id AND l.warehouse_id = s.warehouse_id
        LEFT JOIN products   p ON p.id = COALESCE(s.product_id,   l.product_id)
        LEFT JOIN warehouses w ON w.id = COALESCE(s.warehouse_id, l.warehouse_id)
        WHERE COALESCE(s.quantity_on_hand, 0)  IS DISTINCT FROM COALESCE(l.on_hand, 0)::int
           OR COALESCE(s.quantity_reserved, 0) IS DISTINCT FROM COALESCE(l.reserved, 0)::int
        ORDER BY sku, warehouse_code;
        """;

    /// <summary>
    /// Repair covers all three shapes FindDiscrepanciesAsync can report, not just the
    /// easy one. An UPDATE ... FROM ledger alone fixes a snapshot the ledger disagrees
    /// with, but silently leaves the other two — a snapshot row with no ledger at all,
    /// and a ledger group with no snapshot row — so a second reconcile would still
    /// report a non-zero count after "repaired: true". That is worse than not repairing.
    /// </summary>
    private const string RepairSql = """
        WITH ledger AS (
            SELECT product_id,
                   warehouse_id,
                   SUM(on_hand_delta)::int  AS on_hand,
                   SUM(reserved_delta)::int AS reserved
            FROM stock_movements
            GROUP BY product_id, warehouse_id
        ),
        -- 1. A ledger group with no snapshot row: materialise the row.
        inserted AS (
            INSERT INTO stock_items
                (id, product_id, warehouse_id, quantity_on_hand,
                 quantity_reserved, average_unit_cost_cents, bin_location, last_counted_at)
            SELECT gen_random_uuid(), l.product_id, l.warehouse_id,
                   l.on_hand, l.reserved, 0, NULL, NULL
            FROM ledger l
            WHERE NOT EXISTS (
                SELECT 1 FROM stock_items s
                WHERE s.product_id = l.product_id AND s.warehouse_id = l.warehouse_id)
            RETURNING 1
        ),
        -- 2. A snapshot row the ledger cannot explain at all: zero it.
        zeroed AS (
            UPDATE stock_items s
            SET quantity_on_hand = 0, quantity_reserved = 0
            WHERE NOT EXISTS (
                SELECT 1 FROM ledger l
                WHERE l.product_id = s.product_id AND l.warehouse_id = s.warehouse_id)
              AND (s.quantity_on_hand <> 0 OR s.quantity_reserved <> 0)
            RETURNING 1
        )
        -- 3. The common case: a snapshot the ledger disagrees with.
        UPDATE stock_items s
        SET quantity_on_hand  = l.on_hand,
            quantity_reserved = l.reserved
        FROM ledger l
        WHERE l.product_id = s.product_id
          AND l.warehouse_id = s.warehouse_id
          AND (s.quantity_on_hand  IS DISTINCT FROM l.on_hand
            OR s.quantity_reserved IS DISTINCT FROM l.reserved);
        """;

    public async Task<IReadOnlyList<DiscrepancyDto>> FindDiscrepanciesAsync(CancellationToken ct = default)
    {
        var connection = db.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = FindSql;

        // Enlist in the ambient transaction when there is one: reconcile runs as an
        // ICommand, so TransactionBehavior has already opened one, and a command on the
        // same connection that ignores it fails outright on Npgsql.
        if (db.Database.CurrentTransaction is { } transaction)
            command.Transaction = transaction.GetDbTransaction();

        await using var reader = await command.ExecuteReaderAsync(ct);

        var rows = new List<DiscrepancyDto>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new DiscrepancyDto(
                reader.GetGuid(0), reader.GetString(1),
                reader.GetGuid(2), reader.GetString(3),
                reader.GetInt32(4), reader.GetInt32(5),
                reader.GetInt32(6), reader.GetInt32(7)));
        }

        return rows;
    }

    /// <summary>
    /// EXCLUSIVE, not ACCESS EXCLUSIVE: it conflicts with ROW EXCLUSIVE (every writer) while
    /// still allowing ACCESS SHARE (every reader), so the stock pages keep serving during a
    /// repair. Held to the end of the ambient transaction, which is what closes the window
    /// between the scan and the UPDATE as well as the one inside it.
    /// </summary>
    public Task LockForRepairAsync(CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync(
            "LOCK TABLE stock_items, stock_movements IN EXCLUSIVE MODE;", ct);

    public Task<int> CountStockRowsAsync(CancellationToken ct = default) =>
        db.StockItems.CountAsync(ct);

    public Task<int> RepairAsync(CancellationToken ct = default) =>
        db.Database.ExecuteSqlRawAsync(RepairSql, ct);
}
