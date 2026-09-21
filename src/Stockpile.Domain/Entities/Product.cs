using Stockpile.Domain.Common;
using Stockpile.Domain.ValueObjects;

namespace Stockpile.Domain.Entities;

/// <summary>
/// Edit-style aggregate: two users editing the same product form is a genuine conflict,
/// so this one DOES carry an optimistic concurrency token (xmin). Contrast StockItem,
/// where the same tool would be wrong (§6).
/// </summary>
public sealed class Product : Entity
{
    public const int MaxNameLength = 200;

    private Product() { }   // EF

    public Sku Sku { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Category { get; private set; } = string.Empty;
    public long UnitPriceCents { get; private set; }
    public string? Barcode { get; private set; }
    public int ReorderPoint { get; private set; }
    public int ReorderQuantity { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid CreatedBy { get; private set; }

    /// <summary>Mapped to Postgres <c>xmin</c>; EF treats it as the concurrency token.</summary>
    public uint RowVersion { get; private set; }

    public static Result<Product> Create(
        Sku sku,
        string name,
        string? description,
        string category,
        long unitPriceCents,
        string? barcode,
        int reorderPoint,
        int reorderQuantity,
        Guid createdBy,
        DateTimeOffset createdAt)
    {
        var nameCheck = ValidateName(name);
        if (nameCheck.IsFailure)
            return nameCheck.Error;

        if (unitPriceCents < 0)
            return new DomainRuleError("product.price_negative", "Unit price cannot be negative.");

        var policyCheck = ValidateReorderPolicy(reorderPoint, reorderQuantity);
        if (policyCheck.IsFailure)
            return policyCheck.Error;

        if (string.IsNullOrWhiteSpace(category))
            return new DomainRuleError("product.category_required", "Category is required.");

        return new Product
        {
            Sku = sku,
            Name = name.Trim(),
            Description = description?.Trim(),
            Category = category.Trim(),
            UnitPriceCents = unitPriceCents,
            Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
            ReorderPoint = reorderPoint,
            ReorderQuantity = reorderQuantity,
            IsActive = true,
            CreatedAt = createdAt,
            CreatedBy = createdBy
        };
    }

    public Result Rename(string name)
    {
        var check = ValidateName(name);
        if (check.IsFailure)
            return check;

        Name = name.Trim();
        return Result.Ok();
    }

    public Result SetReorderPolicy(int reorderPoint, int reorderQuantity)
    {
        var check = ValidateReorderPolicy(reorderPoint, reorderQuantity);
        if (check.IsFailure)
            return check;

        ReorderPoint = reorderPoint;
        ReorderQuantity = reorderQuantity;
        return Result.Ok();
    }

    public Result SetCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return new DomainRuleError("product.category_required", "Category is required.");

        Category = category.Trim();
        return Result.Ok();
    }

    /// <summary>Clearing the description is legitimate, so an empty string means "none".</summary>
    public Result SetDescription(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Ok();
    }

    public Result SetPricing(long unitPriceCents)
    {
        if (unitPriceCents < 0)
            return new DomainRuleError("product.price_negative", "Unit price cannot be negative.");

        UnitPriceCents = unitPriceCents;
        return Result.Ok();
    }

    public Result Deactivate()
    {
        if (!IsActive)
            return new DomainRuleError("product.already_inactive", "Product is already inactive.");

        IsActive = false;
        return Result.Ok();
    }

    public Result Reactivate()
    {
        if (IsActive)
            return new DomainRuleError("product.already_active", "Product is already active.");

        IsActive = true;
        return Result.Ok();
    }

    private static Result ValidateName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? new DomainRuleError("product.name_required", "Product name is required.")
            : name.Trim().Length > MaxNameLength
                ? new DomainRuleError("product.name_too_long", $"Name cannot exceed {MaxNameLength} characters.")
                : Result.Ok();

    private static Result ValidateReorderPolicy(int reorderPoint, int reorderQuantity) =>
        reorderPoint < 0 || reorderQuantity <= 0
            ? new DomainRuleError(
                "product.reorder_policy_invalid",
                "Reorder point must be zero or more and reorder quantity must be at least one.")
            : Result.Ok();
}
