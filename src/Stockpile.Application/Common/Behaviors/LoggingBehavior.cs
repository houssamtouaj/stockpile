using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Common.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger,
    ICurrentUser currentUser)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var stopwatch = Stopwatch.StartNew();

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestName"] = requestName,
            ["UserId"] = currentUser.UserId,
            ["CorrelationId"] = currentUser.CorrelationId
        });

        logger.LogInformation("Handling {RequestName}", requestName);

        var response = await next(cancellationToken);
        stopwatch.Stop();

        if (response is Result { IsFailure: true } failed)
        {
            // A refused command logs at Warning with its error code, not at Error.
            // Insufficient stock is a normal outcome; logging it as an error means the
            // first load test fills the log with forty alarms that are all correct.
            logger.LogWarning(
                "{RequestName} refused after {ElapsedMs}ms: {ErrorCode} — {ErrorMessage}",
                requestName, stopwatch.ElapsedMilliseconds, failed.Error.Code, failed.Error.Message);
        }
        else
        {
            logger.LogInformation(
                "{RequestName} completed in {ElapsedMs}ms", requestName, stopwatch.ElapsedMilliseconds);
        }

        return response;
    }
}
