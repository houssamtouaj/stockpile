using System.Reflection;
using Stockpile.Domain.Common;

namespace Stockpile.Application.Common.Behaviors;

/// <summary>
/// Builds a failed Result or Result&lt;T&gt; from an open generic TResponse. Pipeline
/// behaviors short-circuit without knowing the concrete response type, and reflecting
/// once here is cheaper than duplicating every behavior for both shapes.
/// </summary>
internal static class ResultFactory
{
    public static TResponse Failure<TResponse>(Error error)
    {
        var type = typeof(TResponse);

        if (type == typeof(Result))
            return (TResponse)(object)Result.Fail(error);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var method = type.GetMethod(nameof(Result.Fail), BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"No static Fail on {type}.");
            return (TResponse)method.Invoke(null, [error])!;
        }

        throw new InvalidOperationException(
            $"{type.Name} is not a Result type. Every command and query must return Result or Result<T>.");
    }
}
