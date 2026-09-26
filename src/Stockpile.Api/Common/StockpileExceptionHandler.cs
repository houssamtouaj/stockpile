using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Exceptions;
using Stockpile.Domain.Common;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Api.Common;

/// <summary>
/// AddProblemDetails() only shapes responses the framework already produces; it does not
/// put anything in the pipeline that turns an exception into one. Without this handler an
/// exception thrown out of a request leaves as a bare 500 with no body and no errorCode —
/// StockInvariantViolatedException included, whose entire reason for existing is to be
/// attributable to a named constraint.
/// <para>
/// Everything is handled here rather than selectively, because "handled" is what decides
/// whether the caller gets a problem document at all. An unrecognised exception still gets
/// a document and a log line; it just refuses to describe itself, since the message of an
/// unexpected exception is not something to hand to a client.
/// </para>
/// </summary>
internal sealed class StockpileExceptionHandler(ILogger<StockpileExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is StockInvariantViolatedException invariant)
        {
            // The conditional UPDATEs are supposed to make this unreachable, so a breach
            // is a defect report rather than a refusal: logged at Error with the constraint
            // named, and mapped through the same Error path every other failure takes.
            logger.LogError(
                exception,
                "Stock invariant {Constraint} was violated — a writer predicate has a hole. {Method} {Path}",
                invariant.ConstraintName, httpContext.Request.Method, httpContext.Request.Path);

            await new StockInvariantViolatedError(invariant.ConstraintName)
                .ToProblem()
                .ExecuteAsync(httpContext);

            return true;
        }

        if (exception is DbUpdateConcurrencyException concurrency)
        {
            // Two transitions raced on one order: both loaded it, and the loser's UPDATE
            // matched no row on xmin at commit. The transaction has already rolled back —
            // stock mutations included — so this is the ordinary 409 an edit-style
            // aggregate gives, not a fault. Handlers cannot return it themselves because the
            // save that discovers it happens in the unit of work, after they have returned.
            var entity = concurrency.Entries.FirstOrDefault()?.Entity;

            await new ConcurrencyConflictError(
                    entity?.GetType().Name ?? "Entity",
                    (entity as Entity)?.Id ?? Guid.Empty)
                .ToProblem()
                .ExecuteAsync(httpContext);

            return true;
        }

        if (exception is TransientConflictException transient)
        {
            // Every retry in TransactionBehavior collided too. Warning, not Error: nothing
            // is broken, and the client has been told to send the request again.
            logger.LogWarning(
                exception,
                "Transient conflict (SQLSTATE {SqlState}) outlasted the retries. {Method} {Path}",
                transient.SqlState, httpContext.Request.Method, httpContext.Request.Path);

            await new TransientConflictError().ToProblem().ExecuteAsync(httpContext);
            return true;
        }

        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        await Results.Problem(
                title: "Unexpected error",
                detail: "The request could not be completed.",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?> { ["errorCode"] = "unexpected_error" })
            .ExecuteAsync(httpContext);

        return true;
    }
}
