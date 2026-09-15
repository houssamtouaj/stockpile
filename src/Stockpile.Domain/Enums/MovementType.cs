namespace Stockpile.Domain.Enums;

public enum MovementType
{
    Receipt = 0,
    Issue = 1,
    TransferIn = 2,
    TransferOut = 3,
    Adjustment = 4,
    Count = 5,
    ReservationHold = 6,
    ReservationRelease = 7
}
