namespace Stockpile.Infrastructure.Persistence;

/// <summary>
/// Every statement follows the same shape:
///
///   WITH target   -- does the row exist at all?  (distinguishes 404 from 422)
///   , updated     -- the conditional UPDATE; zero rows means the precondition failed
///   SELECT row_exists, on_hand_after, reserved_after, previous_on_hand
///
/// The UPDATE's predicate references only live columns of stock_items (s.*), never a
/// value read earlier in the statement. That matters: under READ COMMITTED, when a
/// concurrent transaction commits and releases the row lock, Postgres re-evaluates the
/// UPDATE's WHERE clause against the NEW row version. A predicate over live columns
/// therefore sees the winner's write and correctly refuses; a predicate over a snapshot
/// taken in a CTE would not, and the race would be back.
/// </summary>
internal static class StockSql
{
    private const string Tail = """
        SELECT
            EXISTS (SELECT 1 FROM target)                  AS row_exists,
            (SELECT quantity_on_hand  FROM updated)        AS on_hand_after,
            (SELECT quantity_reserved FROM updated)        AS reserved_after,
            (SELECT previous_on_hand  FROM updated)        AS previous_on_hand;
        """;

    private const string Target = """
        WITH target AS (
            SELECT id FROM stock_items
            WHERE product_id = @product_id AND warehouse_id = @warehouse_id
        ),
        """;

    public const string EnsureRow = """
        INSERT INTO stock_items
            (id, product_id, warehouse_id, quantity_on_hand,
             quantity_reserved, average_unit_cost_cents, bin_location, last_counted_at)
        VALUES (@id, @product_id, @warehouse_id, 0, 0, 0, NULL, NULL)
        ON CONFLICT (product_id, warehouse_id) DO NOTHING;
        """;

    /// <summary>
    /// Locks a set of rows in id order. The sort sits below the lock in the plan, so rows
    /// are locked as they come out of the ORDER BY — which is what makes the order global.
    /// </summary>
    public const string LockRows = """
        SELECT id FROM stock_items
        WHERE (product_id, warehouse_id) IN (
            SELECT * FROM unnest(@product_ids::uuid[], @warehouse_ids::uuid[]))
        ORDER BY id
        FOR UPDATE;
        """;

    /// <summary>Hold stock for an order. Moves reserved only; on-hand is untouched.</summary>
    public static readonly string Reserve = Target + """
        updated AS (
            UPDATE stock_items s
            SET quantity_reserved = s.quantity_reserved + @quantity
            WHERE s.product_id = @product_id
              AND s.warehouse_id = @warehouse_id
              AND s.quantity_on_hand - s.quantity_reserved >= @quantity
            RETURNING s.quantity_on_hand, s.quantity_reserved,
                      s.quantity_on_hand AS previous_on_hand
        )
        """ + Tail;

    public static readonly string Release = Target + """
        updated AS (
            UPDATE stock_items s
            SET quantity_reserved = s.quantity_reserved - @quantity
            WHERE s.product_id = @product_id
              AND s.warehouse_id = @warehouse_id
              AND s.quantity_reserved >= @quantity
            RETURNING s.quantity_on_hand, s.quantity_reserved,
                      s.quantity_on_hand AS previous_on_hand
        )
        """ + Tail;

    /// <summary>Ship. Converts a reservation into an outbound issue: both counters drop.</summary>
    public static readonly string Issue = Target + """
        updated AS (
            UPDATE stock_items s
            SET quantity_on_hand   = s.quantity_on_hand - @quantity,
                quantity_reserved  = s.quantity_reserved - @quantity
            WHERE s.product_id = @product_id
              AND s.warehouse_id = @warehouse_id
              AND s.quantity_reserved >= @quantity
              AND s.quantity_on_hand  >= @quantity
            RETURNING s.quantity_on_hand, s.quantity_reserved,
                      s.quantity_on_hand + @quantity AS previous_on_hand
        )
        """ + Tail;

    /// <summary>
    /// Receive goods. No availability precondition — receiving always succeeds if the row
    /// exists. The weighted-average recompute uses the PRE-update quantity_on_hand, which
    /// is what a Postgres SET expression evaluates against, and rounds through numeric so
    /// fractional cents do not truncate away over hundreds of receipts (§5).
    /// </summary>
    public static readonly string Receive = Target + """
        updated AS (
            UPDATE stock_items s
            SET quantity_on_hand = s.quantity_on_hand + @quantity,
                average_unit_cost_cents = CASE
                    WHEN s.quantity_on_hand + @quantity <= 0
                        THEN s.average_unit_cost_cents
                    ELSE ROUND(
                        ((s.average_unit_cost_cents::numeric * s.quantity_on_hand)
                         + (@unit_cost_cents::numeric * @quantity))
                        / (s.quantity_on_hand + @quantity)
                    )::bigint
                END
            WHERE s.product_id = @product_id
              AND s.warehouse_id = @warehouse_id
            RETURNING s.quantity_on_hand, s.quantity_reserved,
                      s.quantity_on_hand - @quantity AS previous_on_hand
        )
        """ + Tail;

    /// <summary>
    /// A transfer's outbound leg: the Adjust precondition, plus the receipt's weighted
    /// average run backwards. Removing q units at a known cost leaves the rest valued at
    /// (value - q x cost) / (on_hand - q), so the row gives up exactly the value the inbound
    /// leg adds. When the cost IS the row's average — every dispatch — the average is
    /// unchanged; out of an in-transit row blending several transfers, each receipt takes
    /// its own transfer's value and leaves the others' behind. GREATEST(0, ...) only guards
    /// against rounding drift: a correct ledger never asks for more value than the row holds.
    /// </summary>
    public static readonly string WithdrawAtCost = Target + """
        updated AS (
            UPDATE stock_items s
            SET quantity_on_hand = s.quantity_on_hand - @quantity,
                average_unit_cost_cents = CASE
                    WHEN s.quantity_on_hand - @quantity <= 0
                        THEN s.average_unit_cost_cents
                    ELSE GREATEST(0, ROUND(
                        ((s.average_unit_cost_cents::numeric * s.quantity_on_hand)
                         - (@unit_cost_cents::numeric * @quantity))
                        / (s.quantity_on_hand - @quantity)
                    ))::bigint
                END
            WHERE s.product_id = @product_id
              AND s.warehouse_id = @warehouse_id
              AND s.quantity_on_hand - @quantity >= 0
              AND s.quantity_on_hand - @quantity >= s.quantity_reserved
            RETURNING s.quantity_on_hand, s.quantity_reserved,
                      s.quantity_on_hand + @quantity AS previous_on_hand
        )
        """ + Tail;

    /// <summary>
    /// Signed adjustment with a reason. Refuses any delta that would take on-hand below
    /// zero or below the reserved quantity — reserved units are promised to an order and
    /// cannot be adjusted away without releasing them first.
    /// </summary>
    public static readonly string Adjust = Target + """
        updated AS (
            UPDATE stock_items s
            SET quantity_on_hand = s.quantity_on_hand + @on_hand_delta
            WHERE s.product_id = @product_id
              AND s.warehouse_id = @warehouse_id
              AND s.quantity_on_hand + @on_hand_delta >= 0
              AND s.quantity_on_hand + @on_hand_delta >= s.quantity_reserved
            RETURNING s.quantity_on_hand, s.quantity_reserved,
                      s.quantity_on_hand - @on_hand_delta AS previous_on_hand
        )
        """ + Tail;

    /// <summary>
    /// Cycle count. The ONE statement that locks first, because the ledger delta is
    /// (observed - previous) and RETURNING can only give post-update values. FOR UPDATE
    /// makes the previous value the one this transaction is about to overwrite, rather
    /// than a stale read. Counts are rare and operator-initiated, so the lock costs nothing.
    /// </summary>
    public const string Count = """
        WITH target AS (
            SELECT id, quantity_on_hand AS previous_on_hand
            FROM stock_items
            WHERE product_id = @product_id AND warehouse_id = @warehouse_id
            FOR UPDATE
        ),
        updated AS (
            UPDATE stock_items s
            SET quantity_on_hand = @observed_on_hand,
                last_counted_at  = @counted_at
            FROM target t
            WHERE s.id = t.id
              AND @observed_on_hand >= 0
              AND @observed_on_hand >= s.quantity_reserved
            RETURNING s.quantity_on_hand, s.quantity_reserved, t.previous_on_hand
        )
        SELECT
            EXISTS (SELECT 1 FROM target)                  AS row_exists,
            (SELECT quantity_on_hand  FROM updated)        AS on_hand_after,
            (SELECT quantity_reserved FROM updated)        AS reserved_after,
            (SELECT previous_on_hand  FROM updated)        AS previous_on_hand;
        """;
}
