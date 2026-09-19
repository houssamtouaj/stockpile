using Stockpile.Application.Common.Messaging;

namespace Stockpile.Application.Stock.Commands.ReconcileStock;

public sealed record ReconcileStockCommand(bool Repair = false) : ICommand<ReconciliationReport>;
