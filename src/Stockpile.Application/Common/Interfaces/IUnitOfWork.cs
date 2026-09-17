namespace Stockpile.Application.Common.Interfaces;

/// <summary>
/// Owns the ambient transaction. StockWriter's raw SQL enlists in it (phase 02), which is
/// what keeps the conditional UPDATE and the ledger append atomic with each other.
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}
