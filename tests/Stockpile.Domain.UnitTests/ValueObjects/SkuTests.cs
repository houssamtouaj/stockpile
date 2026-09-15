using Shouldly;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Domain.UnitTests.ValueObjects;

public class SkuTests
{
    [Theory]
    [InlineData("ABC-123")]
    [InlineData("WIDGET1")]
    [InlineData("A1")]
    public void Create_withValidInput_succeeds(string raw)
    {
        var result = Sku.Create(raw);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(raw);
    }

    [Fact]
    public void Create_normalisesToUpperCaseAndTrims()
    {
        Sku.Create("  abc-123  ").Value.Value.ShouldBe("ABC-123");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]                    // too short
    [InlineData("HAS SPACE")]
    [InlineData("HAS_UNDERSCORE")]
    public void Create_withInvalidInput_fails(string? raw)
    {
        var result = Sku.Create(raw);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("sku.invalid");
    }

    [Fact]
    public void Create_withInputLongerThan32Characters_fails()
    {
        Sku.Create(new string('A', 33)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void TwoSkusWithTheSameValue_areEqual()
    {
        Sku.Create("ABC-123").Value.ShouldBe(Sku.Create("abc-123").Value);
    }
}
