using FluentValidation;
using Stockpile.Application.Common.Messaging;
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

        // Used as-is, not derived: one receipt is one movement. The column holds 100.
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}
