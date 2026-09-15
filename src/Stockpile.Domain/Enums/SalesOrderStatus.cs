namespace Stockpile.Domain.Enums;

public enum SalesOrderStatus
{
    Draft = 0,
    Confirmed = 1,
    Picking = 2,
    Packed = 3,
    Shipped = 4,
    Cancelled = 5
}
