using System.Diagnostics.CodeAnalysis;

namespace Stockpile.Domain.Common;

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        if (!isSuccess && error is null)
            throw new ArgumentException("A failed result must carry an error.", nameof(error));

        IsSuccess = isSuccess;
        Error = error;
    }

    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess { get; }

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsFailure => !IsSuccess;

    public Error? Error { get; }

    public static Result Ok() => new(true, null);
    public static Result Fail(Error error) => new(false, error);

    public static implicit operator Result(Error error) => Fail(error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error? error) : base(isSuccess, error)
        => _value = value;

    /// <summary>Throws when the result is a failure — never returns a placeholder.</summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read Value of a failed result. Error: {Error!.Code} — {Error.Message}");

    public static Result<T> Ok(T value) => new(true, value, null);
    public static new Result<T> Fail(Error error) => new(false, default, error);

    public static implicit operator Result<T>(T value) => Ok(value);
    public static implicit operator Result<T>(Error error) => Fail(error);
}
