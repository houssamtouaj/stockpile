namespace Stockpile.Application.SalesOrders.Queries.GetOrderBoard;

public sealed record OrderCardDto(
    Guid Id,
    string Number,
    string CustomerName,
    int LineCount,
    int TotalUnits,
    DateTimeOffset CreatedAt,
    string Status);

/// <summary>One key per open status — always all four, empty or not.</summary>
public sealed record OrderBoardDto(IReadOnlyDictionary<string, IReadOnlyList<OrderCardDto>> Columns);
