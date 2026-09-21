using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Common.Pagination;
using Stockpile.Application.Products.Queries.GetProduct;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Products.Queries.SearchProducts;

public sealed record SearchProductsQuery(
    string? Search = null,
    string? Category = null,
    int Page = 1,
    int Size = 25) : IQuery<Result<PagedList<ProductDto>>>;
