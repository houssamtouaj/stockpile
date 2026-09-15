namespace Stockpile.Domain.Common;

/// <summary>
/// A machine-readable failure. <see cref="Code"/> is what the API surfaces in the
/// RFC 7807 problem document, so clients can branch on it without parsing prose.
/// </summary>
public abstract record Error(string Code, string Message);

public sealed record NotFoundError(string EntityType, Guid Id)
    : Error("not_found", $"{EntityType} '{Id}' was not found.");

public sealed record InsufficientStockError(int Requested, int Available)
    : Error("stock.insufficient",
            $"Requested {Requested} unit(s) but only {Available} are available.");

/// <summary>
/// The database CHECK constraint fired. This should be impossible; if it happens
/// there is a logic bug, not a concurrency event. Maps to 500 plus an alert.
/// </summary>
public sealed record StockInvariantViolatedError(string ConstraintName)
    : Error("stock.invariant_violated",
            $"Stock invariant '{ConstraintName}' was violated. This indicates a defect.");

/// <summary>Optimistic concurrency clash on an edit-style aggregate. Maps to 409.</summary>
public sealed record ConcurrencyConflictError(string EntityType, Guid Id)
    : Error("concurrency.conflict",
            $"{EntityType} '{Id}' was modified by someone else. Reload and retry.");

public sealed record ValidationFailedError(IReadOnlyDictionary<string, string[]> Failures)
    : Error("validation.failed", "One or more fields are invalid.");

/// <summary>A domain rule refused the operation. Maps to 422.</summary>
public sealed record DomainRuleError(string RuleCode, string Detail)
    : Error(RuleCode, Detail);

/// <summary>The caller's role does not permit this. Maps to 403.</summary>
public sealed record ForbiddenError(string Capability)
    : Error("forbidden", $"Your role does not permit '{Capability}'.");
