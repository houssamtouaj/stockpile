using MediatR;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Stock.Commands.ReconcileStock;

public sealed class ReconcileStockHandler(IReconciliationReader reader)
    : IRequestHandler<ReconcileStockCommand, Result<ReconciliationReport>>
{
    public async Task<Result<ReconciliationReport>> Handle(
        ReconcileStockCommand command, CancellationToken cancellationToken)
    {
        // Taken BEFORE the scan, not before the repair: the repair rewrites snapshots from
        // totals the scan computed, so both statements have to see the same world. A
        // read-only reconcile takes nothing — it is a report, and a report that blocks
        // every warehouse to produce itself is not worth having.
        if (command.Repair)
            await reader.LockForRepairAsync(cancellationToken);

        var rowsChecked = await reader.CountStockRowsAsync(cancellationToken);
        var discrepancies = await reader.FindDiscrepanciesAsync(cancellationToken);

        if (command.Repair && discrepancies.Count > 0)
            await reader.RepairAsync(cancellationToken);

        // The discrepancies found BEFORE repairing, so the response documents what was
        // wrong rather than handing back an empty list.
        return new ReconciliationReport(
            rowsChecked,
            discrepancies.Count,
            discrepancies,
            Repaired: command.Repair && discrepancies.Count > 0);
    }
}
