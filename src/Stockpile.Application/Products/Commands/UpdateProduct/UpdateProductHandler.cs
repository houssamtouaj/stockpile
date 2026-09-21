using MediatR;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Application.Products.Queries.GetProduct;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;

namespace Stockpile.Application.Products.Commands.UpdateProduct;

public sealed class UpdateProductHandler(IAppDbContext db)
    : IRequestHandler<UpdateProductCommand, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(
        UpdateProductCommand command, CancellationToken cancellationToken)
    {
        var product = await db.Products
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (product is null)
            return new NotFoundError("Product", command.Id);

        // Pin the token to what the CLIENT last saw, not to what we just read. EF then
        // emits `WHERE id = @id AND xmin = @original`; if someone else committed in
        // between, zero rows match and SaveChanges raises DbUpdateConcurrencyException.
        db.Entry(product).Property(nameof(Product.RowVersion)).OriginalValue = command.RowVersion;

        if (command.Name is not null)
        {
            var renamed = product.Rename(command.Name);
            if (renamed.IsFailure)
                return renamed.Error;
        }

        if (command.Category is not null)
        {
            var categorised = product.SetCategory(command.Category);
            if (categorised.IsFailure)
                return categorised.Error;
        }

        if (command.Description is not null)
            product.SetDescription(command.Description);

        if (command.UnitPriceCents is { } price)
        {
            var priced = product.SetPricing(price);
            if (priced.IsFailure)
                return priced.Error;
        }

        if (command.ReorderPoint is not null || command.ReorderQuantity is not null)
        {
            var policy = product.SetReorderPolicy(
                command.ReorderPoint ?? product.ReorderPoint,
                command.ReorderQuantity ?? product.ReorderQuantity);

            if (policy.IsFailure)
                return policy.Error;
        }

        if (command.IsActive is { } isActive && isActive != product.IsActive)
        {
            var toggled = isActive ? product.Reactivate() : product.Deactivate();
            if (toggled.IsFailure)
                return toggled.Error;
        }

        try
        {
            // Saved here so the conflict surfaces as a Result the endpoint can map to 409,
            // rather than as an exception thrown out of the pipeline's commit.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new ConcurrencyConflictError("Product", command.Id);
        }

        return ProductDto.From(product);
    }
}
