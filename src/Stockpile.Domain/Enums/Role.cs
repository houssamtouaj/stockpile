namespace Stockpile.Domain.Enums;

/// <summary>Ordered least to most privileged; each role inherits everything below it
/// (§8 permission matrix, mirrored by the actor generalisation in the use-case diagram).</summary>
public enum Role
{
    Viewer = 0,
    Operator = 1,
    WarehouseManager = 2,
    Admin = 3
}
