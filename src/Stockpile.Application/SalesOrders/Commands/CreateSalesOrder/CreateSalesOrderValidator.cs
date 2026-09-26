using FluentValidation;

namespace Stockpile.Application.SalesOrders.Commands.CreateSalesOrder;

public sealed class CreateSalesOrderValidator : AbstractValidator<CreateSalesOrderCommand>
{
    public CreateSalesOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();

        // Emptiness and duplicate products are domain rules (so.lines_required,
        // so.duplicate_product_line) and come back as 422 from the aggregate. What is
        // checked here is shape: a request that could never be a valid line.
        RuleFor(x => x.Lines).NotNull().Must(l => l is null || l.Count <= 500)
            .WithMessage("A sales order may have at most 500 lines.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0).LessThanOrEqualTo(1_000_000);
            line.RuleFor(l => l.UnitPriceCents).GreaterThanOrEqualTo(0);
        });
    }
}
