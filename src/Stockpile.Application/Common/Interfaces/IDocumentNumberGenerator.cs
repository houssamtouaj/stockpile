namespace Stockpile.Application.Common.Interfaces;

/// <summary>
/// Human-readable, unique document numbers such as <c>PO-2026-00001</c>. Prefixes are a
/// closed set — "PO", "SO" and "TR" — and anything else throws, because an unknown prefix
/// is a programming error rather than a request a caller could correct.
/// </summary>
public interface IDocumentNumberGenerator
{
    Task<string> NextAsync(string prefix, CancellationToken ct = default);
}
