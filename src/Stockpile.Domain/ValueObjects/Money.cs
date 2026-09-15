namespace Stockpile.Domain.ValueObjects;

/// <summary>Integer cents only. Single currency — multi-currency is out of scope (§2).</summary>
public readonly record struct Money
{
    public const string DefaultCurrency = "EUR";

    private Money(long cents, string currency)
    {
        Cents = cents;
        Currency = currency;
    }

    public long Cents { get; }
    public string Currency { get; }

    public static Money Zero => new(0, DefaultCurrency);

    public static Money FromCents(long cents, string currency = DefaultCurrency) =>
        new(cents, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Cents + other.Cents, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Cents - other.Cents, Currency);
    }

    public Money Multiply(int factor) => new(Cents * factor, Currency);

    private void EnsureSameCurrency(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Cannot combine {Currency} with {other.Currency}; multi-currency is out of scope.");
    }

    public override string ToString() => $"{Cents / 100m:0.00} {Currency}";
}
