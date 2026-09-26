using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;

namespace Stockpile.Infrastructure.Persistence;

/// <summary>One idempotency key a movement-less command has already acted on.</summary>
public sealed class ProcessedRequest
{
    private ProcessedRequest() { }   // EF

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; private set; }

    internal static ProcessedRequest Create(string idempotencyKey, string requestHash, DateTimeOffset processedAt) =>
        new() { IdempotencyKey = idempotencyKey, RequestHash = requestHash, ProcessedAt = processedAt };
}

/// <summary>
/// Two concurrent first attempts both see <see cref="ProcessedRequestMatch.New"/>; the
/// primary key lets only one commit. Its name carries "idempotency_key", so the loser is
/// caught by the unit of work's replay path, rolled back and re-run — and the re-run finds
/// the winner's record.
/// </summary>
internal sealed class ProcessedRequestStore(AppDbContext db, IClock clock) : IProcessedRequestStore
{
    public async Task<ProcessedRequestMatch> MatchAsync(
        string idempotencyKey, string fingerprint, CancellationToken ct = default)
    {
        var recorded = await db.ProcessedRequests
            .AsNoTracking()
            .Where(r => r.IdempotencyKey == idempotencyKey)
            .Select(r => r.RequestHash)
            .FirstOrDefaultAsync(ct);

        if (recorded is null)
            return ProcessedRequestMatch.New;

        return recorded == HashOf(fingerprint)
            ? ProcessedRequestMatch.SameRequest
            : ProcessedRequestMatch.DifferentRequest;
    }

    public void Record(string idempotencyKey, string fingerprint) =>
        db.ProcessedRequests.Add(ProcessedRequest.Create(idempotencyKey, HashOf(fingerprint), clock.UtcNow));

    private static string HashOf(string fingerprint) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint)));
}
