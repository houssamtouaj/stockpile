using FluentValidation;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

namespace Stockpile.Application.SalesOrders.Commands.ConfirmSalesOrder;

public sealed record ConfirmSalesOrderCommand(Guid SalesOrderId, string IdempotencyKey)
    : ICommand<SalesOrderDto>;

public sealed class ConfirmSalesOrderValidator : AbstractValidator<ConfirmSalesOrderCommand>
{
    public ConfirmSalesOrderValidator()
    {
        RuleFor(x => x.SalesOrderId).NotEmpty();

        // §6: confirm reserves stock, and a retry without a key double-reserves.
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(DerivedIdempotencyKey.MaxClientKeyLength);
    }
}
