using FluentValidation;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

namespace Stockpile.Application.SalesOrders.Commands.CancelSalesOrder;

public sealed record CancelSalesOrderCommand(Guid SalesOrderId, string IdempotencyKey)
    : ICommand<SalesOrderDto>;

public sealed class CancelSalesOrderValidator : AbstractValidator<CancelSalesOrderCommand>
{
    public CancelSalesOrderValidator()
    {
        RuleFor(x => x.SalesOrderId).NotEmpty();

        // Required even for a draft, which releases nothing: the client cannot know
        // whether someone confirmed the order since it last looked.
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(DerivedIdempotencyKey.MaxClientKeyLength);
    }
}
