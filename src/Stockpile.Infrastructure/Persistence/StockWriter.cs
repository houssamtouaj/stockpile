using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Stockpile.Application.Common.Interfaces;

namespace Stockpile.Infrastructure.Persistence;

/// <summary>
/// Runs every stock mutation on the DbContext's OWN connection, enlisted in the ambient
/// transaction opened by TransactionBehavior.
/// <para>
/// The spec's §6 sample used a separate Dapper connection. That would put the conditional
/// UPDATE in its own transaction, committing the moment it ran — so a later failure in the
/// same handler would leave stock moved with no ledger row behind it, and the reconcile
/// endpoint would report a discrepancy it could not explain. See docs/backend/00-overview.md.
/// </para>
/// </summary>
internal sealed class StockWriter(AppDbContext db) : IStockWriter
{
    public async Task EnsureStockItemAsync(
        Guid productId, Guid warehouseId, CancellationToken ct = default)
    {
        await using var command = await CreateCommandAsync(StockSql.EnsureRow, ct);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("warehouse_id", warehouseId);

        await command.ExecuteNonQueryAsync(ct);
    }

    public Task<StockWriteResult> TryReserveAsync(
        Guid productId, Guid warehouseId, int quantity, CancellationToken ct = default) =>
        ExecuteAsync(StockSql.Reserve, productId, warehouseId, ct,
            ("quantity", quantity));

    public Task<StockWriteResult> TryReleaseAsync(
        Guid productId, Guid warehouseId, int quantity, CancellationToken ct = default) =>
        ExecuteAsync(StockSql.Release, productId, warehouseId, ct,
            ("quantity", quantity));

    public Task<StockWriteResult> TryIssueAsync(
        Guid productId, Guid warehouseId, int quantity, CancellationToken ct = default) =>
        ExecuteAsync(StockSql.Issue, productId, warehouseId, ct,
            ("quantity", quantity));

    public Task<StockWriteResult> TryReceiveAsync(
        Guid productId, Guid warehouseId, int quantity, long unitCostCents, CancellationToken ct = default) =>
        ExecuteAsync(StockSql.Receive, productId, warehouseId, ct,
            ("quantity", quantity), ("unit_cost_cents", unitCostCents));

    public Task<StockWriteResult> TryAdjustAsync(
        Guid productId, Guid warehouseId, int onHandDelta, CancellationToken ct = default) =>
        ExecuteAsync(StockSql.Adjust, productId, warehouseId, ct,
            ("on_hand_delta", onHandDelta));

    public Task<StockWriteResult> TryCountAsync(
        Guid productId, Guid warehouseId, int observedOnHand, DateTimeOffset countedAt,
        CancellationToken ct = default) =>
        ExecuteAsync(StockSql.Count, productId, warehouseId, ct,
            ("observed_on_hand", observedOnHand), ("counted_at", countedAt));

    private async Task<StockWriteResult> ExecuteAsync(
        string sql,
        Guid productId,
        Guid warehouseId,
        CancellationToken ct,
        params (string Name, object Value)[] extraParameters)
    {
        await using var command = await CreateCommandAsync(sql, ct);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("warehouse_id", warehouseId);

        foreach (var (name, value) in extraParameters)
            command.Parameters.AddWithValue(name, value);

        await using var reader = await command.ExecuteReaderAsync(ct);

        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException(
                "The stock statement returned no row. This is a defect in StockSql.");

        var rowExists = reader.GetBoolean(0);
        if (!rowExists)
            return new StockWriteResult(StockWriteOutcome.RowMissing, 0, 0, 0);

        // Nulls here mean the UPDATE matched zero rows: the precondition was false.
        if (await reader.IsDBNullAsync(1, ct))
            return new StockWriteResult(StockWriteOutcome.InsufficientStock, 0, 0, 0);

        return new StockWriteResult(
            StockWriteOutcome.Applied,
            OnHandAfter: reader.GetInt32(1),
            ReservedAfter: reader.GetInt32(2),
            PreviousOnHand: reader.GetInt32(3));
    }

    private async Task<NpgsqlCommand> CreateCommandAsync(string sql, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "IStockWriter was called outside a transaction. Stock mutations must run "
                + "inside the ambient transaction opened by TransactionBehavior, so the "
                + "snapshot write and the ledger append are atomic with each other. "
                + "Make the request an ICommand, or wrap the call in IUnitOfWork.");

        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        return command;
    }
}
