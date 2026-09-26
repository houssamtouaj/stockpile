using FluentValidation;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

namespace Stockpile.Application.SalesOrders.Commands.ShipSalesOrder;

public sealed record ShipSalesOrderCommand(Guid SalesOrderId, string IdempotencyKey)
    : ICommand<SalesOrderDto>;

public sealed class ShipSalesOrderValidator : AbstractValidator<ShipSalesOrderCommand>
{
    public ShipSalesOrderValidator()
    {
        RuleFor(x => x.SalesOrderId).NotEmpty();
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(DerivedIdempotencyKey.MaxClientKeyLength);
    }
}
