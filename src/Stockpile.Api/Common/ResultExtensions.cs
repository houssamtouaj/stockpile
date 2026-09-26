using Stockpile.Domain.Common;

namespace Stockpile.Api.Common;

public static class ResultExtensions
{
    public static IResult ToHttpResult(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error);

    public static IResult ToHttpResult<T>(this Result<T> result, Func<T, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error);

    public static IResult ToOk<T>(this Result<T> result) =>
        result.ToHttpResult(Results.Ok);

    public static IResult ToCreated<T>(this Result<T> result, Func<T, string> location) =>
        result.ToHttpResult(value => Results.Created(location(value), value));

    /// <summary>
    /// The same mapping, for the one caller that does not hold a Result: the exception
    /// handler. Routing it through here is what keeps "single Result to RFC 7807 mapping"
    /// true once exceptions also produce problem documents.
    /// </summary>
    public static IResult ToProblem(this Error error) => Problem(error);

    private static IResult Problem(Error error)
    {
        var (status, title) = Map(error);

        var extensions = new Dictionary<string, object?> { ["errorCode"] = error.Code };

        switch (error)
        {
            case ValidationFailedError validation:
                extensions["errors"] = validation.Failures;
                break;

            case ConcurrencyConflictError conflict:
                // §8: 409 carries enough for the client to reconcile without a second GET.
                extensions["entityType"] = conflict.EntityType;
                extensions["entityId"] = conflict.Id;
                break;

            case StockItemNotFoundError missing:
                extensions["productId"] = missing.ProductId;
                extensions["warehouseId"] = missing.WarehouseId;
                break;

            case IdempotencyKeyReusedError reused:
                extensions["idempotencyKey"] = reused.IdempotencyKey;
                break;

            case InsufficientStockError insufficient:
                extensions["requested"] = insufficient.Requested;
                extensions["available"] = insufficient.Available;
                break;
        }

        return Results.Problem(
            title: title,
            detail: error.Message,
            statusCode: status,
            extensions: extensions);
    }

    private static (int Status, string Title) Map(Error error) => error switch
    {
        ValidationFailedError => (StatusCodes.Status400BadRequest, "Invalid request"),
        NotFoundError or StockItemNotFoundError => (StatusCodes.Status404NotFound, "Not found"),
        ForbiddenError => (StatusCodes.Status403Forbidden, "Forbidden"),

        // §8: a genuine optimistic-concurrency clash on an edit-style aggregate.
        // Nothing in the stock-mutation path ever produces this.
        ConcurrencyConflictError => (StatusCodes.Status409Conflict, "Conflict"),

        // A key replayed with a different request is the other genuine 409: the server
        // cannot satisfy both meanings of one key, and quietly picking the first is how a
        // mutation goes missing with nothing recorded anywhere.
        IdempotencyKeyReusedError => (StatusCodes.Status409Conflict, "Conflict"),

        // Rolled back by a deadlock or serialization failure that outlasted the retries.
        // A concurrency outcome, not a fault — the request was valid and may be resent.
        TransientConflictError => (StatusCodes.Status409Conflict, "Conflict"),

        // §6: insufficient stock is a correct refusal, not a conflict. 422, never 409.
        InsufficientStockError => (StatusCodes.Status422UnprocessableEntity, "Insufficient stock"),

        // The CHECK constraint fired. This should be impossible; treat it as a defect.
        StockInvariantViolatedError => (StatusCodes.Status500InternalServerError, "Stock invariant violated"),

        // Every other domain rule.
        _ => (StatusCodes.Status422UnprocessableEntity, "Request refused")
    };
}
