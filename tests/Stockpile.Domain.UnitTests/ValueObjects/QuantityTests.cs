using Shouldly;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Domain.UnitTests.ValueObjects;

public class QuantityTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public void Create_withPositiveValue_succeeds(int value)
    {
        Quantity.Create(value).Value.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_withNonPositiveValue_fails(int value)
    {
        var result = Quantity.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("quantity.invalid");
    }

    [Fact]
    public void Create_aboveTheSanityCeiling_fails()
    {
        Quantity.Create(1_000_001).IsFailure.ShouldBeTrue();
    }
}
