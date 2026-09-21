using Stockpile.Application.Common.Messaging;
using Stockpile.Application.Products.Queries.GetProduct;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Products.Queries.GetProductByBarcode;

public sealed record GetProductByBarcodeQuery(string Barcode) : IQuery<Result<ProductDto>>;
