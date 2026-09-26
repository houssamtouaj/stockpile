using FluentValidation;
using Stockpile.Application.Common.Messaging;
using Stockpile.Application.PurchaseOrders.Queries.GetPurchaseOrder;

namespace Stockpile.Application.PurchaseOrders.Commands.CreatePurchaseOrder;

public sealed record CreatePurchaseOrderLine(Guid ProductId, int Quantity, long UnitCostCents);

public sealed record CreatePurchaseOrderCommand(
    Guid SupplierId,
    Guid WarehouseId,
    IReadOnlyList<CreatePurchaseOrderLine> Lines,
    DateTimeOffset? ExpectedAt = null) : ICommand<PurchaseOrderDto>;

public sealed class CreatePurchaseOrderValidator : AbstractValidator<CreatePurchaseOrderCommand>
{
    public CreatePurchaseOrderValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();

        // Emptiness and duplicate products are domain rules and come back as 422.
        RuleFor(x => x.Lines).NotNull().Must(l => l is null || l.Count <= 500)
            .WithMessage("A purchase order may have at most 500 lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
            line.RuleFor(l => l.UnitCostCents).GreaterThanOrEqualTo(0);
        });
    }
}
