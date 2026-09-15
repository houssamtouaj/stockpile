using Stockpile.Domain.Common;

namespace Stockpile.Domain.ValueObjects;

/// <summary>A strictly positive count of units. Zero is rejected: a zero-unit movement
/// is always a caller bug and would pollute an append-only ledger.</summary>
public readonly record struct Quantity
{
    public const int Max = 1_000_000;

    private Quantity(int value) => Value = value;

    public int Value { get; }

    public static Result<Quantity> Create(int value) =>
        value is <= 0 or > Max
            ? new DomainRuleError("quantity.invalid", $"Quantity must be between 1 and {Max}.")
            : new Quantity(value);

    public override string ToString() => Value.ToString();
}
