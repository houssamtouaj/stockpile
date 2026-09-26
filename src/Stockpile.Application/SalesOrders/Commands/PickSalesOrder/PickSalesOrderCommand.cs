using FluentValidation;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

namespace Stockpile.Application.SalesOrders.Commands.PickSalesOrder;

/// <summary>
/// No idempotency key: picking writes no stock movement, and a replayed pick is refused by
/// the aggregate once it would exceed the ordered quantity.
/// </summary>
public sealed record PickSalesOrderCommand(Guid SalesOrderId, Guid LineId, int Quantity)
    : ICommand<SalesOrderDto>;

public sealed class PickSalesOrderValidator : AbstractValidator<PickSalesOrderCommand>
{
    public PickSalesOrderValidator()
    {
        RuleFor(x => x.SalesOrderId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
    }
}
