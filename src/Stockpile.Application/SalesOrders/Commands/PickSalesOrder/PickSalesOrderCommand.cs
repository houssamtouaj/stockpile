using FluentValidation;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.SalesOrders.Queries.GetSalesOrder;

namespace Stockpile.Application.SalesOrders.Commands.PickSalesOrder;

/// <summary>
/// Picks accumulate, so a keyless retry of a pick whose response was lost counts the units
/// twice — the aggregate only refuses one that would exceed the ordered quantity. Sending
/// an idempotency key makes a retry replay instead. The key is optional because picks
/// write no stock movement and the §8 contract predates it; a client that retries must
/// send one.
/// </summary>
public sealed record PickSalesOrderCommand(Guid SalesOrderId, Guid LineId, int Quantity, string? IdempotencyKey = null)
    : ICommand<SalesOrderDto>;

public sealed class PickSalesOrderValidator : AbstractValidator<PickSalesOrderCommand>
{
    public PickSalesOrderValidator()
    {
        RuleFor(x => x.SalesOrderId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(DerivedIdempotencyKey.MaxClientKeyLength)
            .When(x => x.IdempotencyKey is not null);
    }
}
