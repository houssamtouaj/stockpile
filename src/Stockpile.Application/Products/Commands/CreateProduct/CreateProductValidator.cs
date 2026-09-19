using FluentValidation;

namespace Stockpile.Application.Products.Commands.CreateProduct;

/// <summary>
/// The validator checks <em>shape</em>; the domain factory checks <em>rules</em>. They
/// overlap, and that is fine: this produces a 400 with field-level messages a form can
/// render, while the factory guarantees no invalid Product can exist even when one is
/// constructed from a seeder.
/// </summary>
public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
        RuleFor(x => x.UnitPriceCents).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderPoint).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ReorderQuantity).GreaterThan(0);
        RuleFor(x => x.Barcode).MaximumLength(64);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}
