using Shouldly;
using Stockpile.Domain.Common;
using Stockpile.Domain.Entities;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Domain.UnitTests.Entities;

public class ProductTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);

    private static Result<Product> Build(
        string name = "Steel Widget",
        long unitPriceCents = 1999,
        int reorderPoint = 20,
        int reorderQuantity = 100) =>
        Product.Create(
            Sku.Create("WID-001").Value,
            name,
            description: "A widget made of steel",
            category: "Hardware",
            unitPriceCents: unitPriceCents,
            barcode: "5012345678900",
            reorderPoint: reorderPoint,
            reorderQuantity: reorderQuantity,
            createdBy: Guid.CreateVersion7(),
            createdAt: Now);

    [Fact]
    public void Create_withValidInput_succeeds_andIsActive()
    {
        var product = Build().Value;

        product.Sku.Value.ShouldBe("WID-001");
        product.Name.ShouldBe("Steel Widget");
        product.IsActive.ShouldBeTrue();
        product.CreatedAt.ShouldBe(Now);
    }

    [Fact]
    public void Create_withBlankName_fails()
    {
        var result = Build(name: "   ");

        result.IsFailure.ShouldBeTrue();
        result.Error!.Code.ShouldBe("product.name_required");
    }

    [Fact]
    public void Create_withNegativePrice_fails()
    {
        Build(unitPriceCents: -1).Error!.Code.ShouldBe("product.price_negative");
    }

    [Fact]
    public void Create_withNegativeReorderPoint_fails()
    {
        Build(reorderPoint: -1).Error!.Code.ShouldBe("product.reorder_policy_invalid");
    }

    [Fact]
    public void Create_withZeroReorderQuantity_fails()
    {
        // Reordering zero units is a policy that can never resolve a low-stock alert.
        Build(reorderQuantity: 0).Error!.Code.ShouldBe("product.reorder_policy_invalid");
    }

    [Fact]
    public void Rename_withValidName_updatesIt()
    {
        var product = Build().Value;

        product.Rename("Brass Widget").IsSuccess.ShouldBeTrue();

        product.Name.ShouldBe("Brass Widget");
    }

    [Fact]
    public void Rename_withBlankName_fails_andLeavesTheOldName()
    {
        var product = Build().Value;

        product.Rename("").IsFailure.ShouldBeTrue();

        product.Name.ShouldBe("Steel Widget");
    }

    [Fact]
    public void Deactivate_thenReactivate_roundTrips()
    {
        var product = Build().Value;

        product.Deactivate().IsSuccess.ShouldBeTrue();
        product.IsActive.ShouldBeFalse();

        product.Reactivate().IsSuccess.ShouldBeTrue();
        product.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Deactivate_whenAlreadyInactive_fails()
    {
        var product = Build().Value;
        product.Deactivate();

        product.Deactivate().Error!.Code.ShouldBe("product.already_inactive");
    }
}
