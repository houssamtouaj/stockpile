using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Customers.Queries.SearchCustomers;

public sealed record SearchCustomersQuery(
    string? Search = null,
    int Page = 1,
    int Size = 25) : IQuery<Result<PagedList<CustomerDto>>>;
