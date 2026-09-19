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
        NotFoundError => (StatusCodes.Status404NotFound, "Not found"),
        ForbiddenError => (StatusCodes.Status403Forbidden, "Forbidden"),

        // §8: a genuine optimistic-concurrency clash on an edit-style aggregate.
        // Nothing in the stock-mutation path ever produces this.
        ConcurrencyConflictError => (StatusCodes.Status409Conflict, "Conflict"),

        // §6: insufficient stock is a correct refusal, not a conflict. 422, never 409.
        InsufficientStockError => (StatusCodes.Status422UnprocessableEntity, "Insufficient stock"),

        // The CHECK constraint fired. This should be impossible; treat it as a defect.
        StockInvariantViolatedError => (StatusCodes.Status500InternalServerError, "Stock invariant violated"),

        // Every other domain rule.
        _ => (StatusCodes.Status422UnprocessableEntity, "Request refused")
    };
}
