using FluentValidation;

namespace Stockpile.Application.Stock.Commands.CountStock;

public sealed class CountStockValidator : AbstractValidator<CountStockCommand>
{
    public CountStockValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();

        // A count is an absolute observed quantity, never a delta, so zero is legitimate
        // and negative is not.
        RuleFor(x => x.ObservedOnHand).GreaterThanOrEqualTo(0).LessThanOrEqualTo(100_000_000);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.IdempotencyKey).NotEmpty().MaximumLength(100);
    }
}
