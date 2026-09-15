using Shouldly;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Domain.UnitTests.ValueObjects;

public class MoneyTests
{
    [Fact]
    public void Add_sumsCents()
    {
        var total = Money.FromCents(1250).Add(Money.FromCents(399));

        total.Cents.ShouldBe(1649);
    }

    [Fact]
    public void Multiply_scalesCents()
    {
        Money.FromCents(1250).Multiply(3).Cents.ShouldBe(3750);
    }

    [Fact]
    public void Add_withDifferentCurrencies_throws()
    {
        var eur = Money.FromCents(100, "EUR");
        var usd = Money.FromCents(100, "USD");

        Should.Throw<InvalidOperationException>(() => eur.Add(usd));
    }

    [Fact]
    public void Zero_isZeroCentsInTheDefaultCurrency()
    {
        Money.Zero.Cents.ShouldBe(0);
        Money.Zero.Currency.ShouldBe("EUR");
    }
}
