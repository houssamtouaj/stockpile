namespace Stockpile.Domain.Common;

/// <summary>
/// A machine-readable failure. <see cref="Code"/> is what the API surfaces in the
/// RFC 7807 problem document, so clients can branch on it without parsing prose.
/// </summary>
public abstract record Error(string Code, string Message);

public sealed record NotFoundError(string EntityType, Guid Id)
    : Error("not_found", $"{EntityType} '{Id}' was not found.");

/// <summary>
/// No stock row exists for this (product, warehouse) pairing. Separate from
/// <see cref="NotFoundError"/> because the missing thing is the pairing, not either id on
/// its own: "StockItem '&lt;a product id&gt;' was not found" sends whoever reads it looking
/// up a StockItem by an id that was never a StockItem id, and says nothing about which
/// warehouse was asked for.
/// </summary>
public sealed record StockItemNotFoundError(Guid ProductId, Guid WarehouseId)
    : Error("not_found",
            $"No stock record exists for product '{ProductId}' in warehouse '{WarehouseId}'.");

/// <summary>
/// An idempotency key was replayed with a different request. Returning the first request's
/// result would silently drop the second one; the key identifies an attempt, not a licence
/// to substitute one mutation for another. Maps to 409.
/// </summary>
public sealed record IdempotencyKeyReusedError(string IdempotencyKey)
    : Error("idempotency.key_reused",
            $"Idempotency key '{IdempotencyKey}' was already used for a different request. "
            + "Use a new key, or resend the original request unchanged.");

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
