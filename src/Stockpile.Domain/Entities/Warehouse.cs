using Stockpile.Domain.Common;
using Stockpile.Domain.Enums;

namespace Stockpile.Domain.Entities;

public sealed class Warehouse : Entity
{
    private Warehouse() { }   // EF

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public WarehouseKind Kind { get; private set; }
    public bool IsActive { get; private set; }

    public bool IsPhysical() => Kind == WarehouseKind.Physical;

    public static Result<Warehouse> CreatePhysical(string code, string name, string? address) =>
        Create(code, name, address, WarehouseKind.Physical);

    /// <summary>
    /// The pseudo-warehouse that holds units mid-transfer. Without it, dispatched stock
    /// exists in no StockItem row and cross-warehouse valuation silently under-reports
    /// (§5 correction 2). It has no address because nobody can walk into it.
    /// </summary>
    public static Result<Warehouse> CreateInTransit(string code, string name) =>
        Create(code, name, address: null, WarehouseKind.InTransit);

    private static Result<Warehouse> Create(string code, string name, string? address, WarehouseKind kind)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new DomainRuleError("warehouse.code_required", "Warehouse code is required.");

        if (string.IsNullOrWhiteSpace(name))
            return new DomainRuleError("warehouse.name_required", "Warehouse name is required.");

        return new Warehouse
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            Kind = kind,
            IsActive = true
        };
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return new DomainRuleError("warehouse.already_inactive", "Warehouse is already inactive.");

        IsActive = false;
        return Result.Ok();
    }
}
