using FluentValidation;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Stock;
using Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;

namespace Stockpile.Application.PurchaseOrders.Commands.ReceivePurchaseOrder;

/// <summary>One delivery against one line. Repeatable until the line is complete.</summary>
public sealed record ReceivePurchaseOrderCommand(
    Guid PurchaseOrderId,
    Guid LineId,
    int Quantity,
    string IdempotencyKey) : ICommand<PurchaseOrderDto>;

public sealed class ReceivePurchaseOrderValidator : AbstractValidator<ReceivePurchaseOrderCommand>
{
    public ReceivePurchaseOrderValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
        RuleFor(x => x.LineId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);

        // Used as-is, not derived — one receipt is one movement — but capped like every
        // other order transition, so a client needs one rule for order keys, not two.
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(DerivedIdempotencyKey.MaxClientKeyLength);
    }
}
