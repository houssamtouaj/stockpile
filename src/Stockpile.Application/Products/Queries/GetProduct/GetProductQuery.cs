using Stockpile.Application.Common.Messaging;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Products.Queries.GetProduct;

public sealed record GetProductQuery(Guid Id) : IQuery<Result<ProductDto>>;
