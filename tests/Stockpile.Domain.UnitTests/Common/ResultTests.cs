using Shouldly;
using Stockpile.Domain.Common;

namespace Stockpile.Domain.UnitTests.Common;

public class ResultTests
{
    [Fact]
    public void Ok_reportsSuccess_andHasNoError()
    {
        var result = Result.Ok();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBeNull();
    }

    [Fact]
    public void Fail_reportsFailure_andCarriesTheError()
    {
        var error = new InsufficientStockError(Requested: 5, Available: 2);

        var result = Result.Fail(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
        result.Error!.Code.ShouldBe("stock.insufficient");
    }

    [Fact]
    public void GenericOk_exposesTheValue()
    {
        var result = Result<int>.Ok(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void GenericValue_whenFailed_throws()
    {
        var result = Result<int>.Fail(new NotFoundError("Product", Guid.Empty));

        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void ErrorConvertsImplicitlyToFailedResult()
    {
        Result<int> result = new NotFoundError("Product", Guid.Empty);

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("not_found");
    }
}
