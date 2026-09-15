using Shouldly;
using Stockpile.Domain.Entities;
using Stockpile.Domain.Enums;

namespace Stockpile.Domain.UnitTests.Entities;

public class WarehouseTests
{
    [Fact]
    public void CreatePhysical_producesAPhysicalWarehouse()
    {
        var warehouse = Warehouse.CreatePhysical("PAR-01", "Paris Nord", "12 rue de Flandre").Value;

        warehouse.Kind.ShouldBe(WarehouseKind.Physical);
        warehouse.IsPhysical().ShouldBeTrue();
        warehouse.Code.ShouldBe("PAR-01");
        warehouse.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void CreateInTransit_producesANonPhysicalWarehouse_withNoAddress()
    {
        var warehouse = Warehouse.CreateInTransit("TRANSIT", "In transit").Value;

        warehouse.Kind.ShouldBe(WarehouseKind.InTransit);
        warehouse.IsPhysical().ShouldBeFalse();
        warehouse.Address.ShouldBeNull();
    }

    [Fact]
    public void Code_isNormalisedToUpperCase()
    {
        Warehouse.CreatePhysical("par-01", "Paris Nord", null).Value.Code.ShouldBe("PAR-01");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreatePhysical_withBlankCode_fails(string code)
    {
        Warehouse.CreatePhysical(code, "Paris Nord", null).Error!.Code.ShouldBe("warehouse.code_required");
    }

    [Fact]
    public void CreatePhysical_withBlankName_fails()
    {
        Warehouse.CreatePhysical("PAR-01", " ", null).Error!.Code.ShouldBe("warehouse.name_required");
    }
}
