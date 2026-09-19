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
