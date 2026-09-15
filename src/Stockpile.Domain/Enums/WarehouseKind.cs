namespace Stockpile.Domain.Enums;

public enum WarehouseKind
{
    /// <summary>A real location that staff can walk into.</summary>
    Physical = 0,

    /// <summary>Holds units that have left one warehouse and not yet arrived at another,
    /// so total valuation stays conserved mid-transfer (§5 correction 2).</summary>
    InTransit = 1
}
