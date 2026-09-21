using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Pagination;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Suppliers.Queries.SearchSuppliers;

public sealed record SearchSuppliersQuery(
    string? Search = null,
    int Page = 1,
    int Size = 25) : IQuery<Result<PagedList<SupplierDto>>>;
