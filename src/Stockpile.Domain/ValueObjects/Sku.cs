using System.Text.RegularExpressions;
using Stockpile.Domain.Common;

namespace Stockpile.Domain.ValueObjects;

public sealed partial record Sku
{
    public const int MaxLength = 32;
    public const int MinLength = 2;

    private Sku(string value) => Value = value;

    public string Value { get; }

    public static Result<Sku> Create(string? raw)
    {
        var normalised = raw?.Trim().ToUpperInvariant() ?? string.Empty;

        if (normalised.Length is < MinLength or > MaxLength || !Pattern().IsMatch(normalised))
            return new DomainRuleError(
                "sku.invalid",
                $"SKU must be {MinLength}-{MaxLength} characters of A-Z, 0-9 or '-'.");

        return new Sku(normalised);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[A-Z0-9-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
