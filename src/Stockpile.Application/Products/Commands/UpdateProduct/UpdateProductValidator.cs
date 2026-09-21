using FluentValidation;

namespace Stockpile.Application.Products.Commands.UpdateProduct;

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        // Only validate the fields the caller actually sent.
        RuleFor(x => x.Name!).NotEmpty().MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Category!).NotEmpty().MaximumLength(100).When(x => x.Category is not null);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.UnitPriceCents!.Value).GreaterThanOrEqualTo(0).When(x => x.UnitPriceCents is not null);
        RuleFor(x => x.ReorderPoint!.Value).GreaterThanOrEqualTo(0).When(x => x.ReorderPoint is not null);
        RuleFor(x => x.ReorderQuantity!.Value).GreaterThan(0).When(x => x.ReorderQuantity is not null);
    }
}
