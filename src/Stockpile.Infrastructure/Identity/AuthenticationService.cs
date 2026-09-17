using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Domain.Common;
using Stockpile.Infrastructure.Persistence;

namespace Stockpile.Infrastructure.Identity;

public sealed record AuthenticatedUser(Guid Id, string Email, string DisplayName, string Role);

public sealed record AuthTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    AuthenticatedUser User);

/// <summary>
/// Authentication is the one feature that does NOT go through MediatR. It depends on
/// UserManager, which is ASP.NET Core Identity — an Infrastructure concern that
/// Application is forbidden from referencing. Routing it through a command would mean
/// inventing an IPasswordVerifier abstraction whose only implementation wraps UserManager
/// and whose only caller is the auth endpoint. So the logic lives here, returns
/// Result&lt;T&gt; like everything else, and the endpoints call it directly.
/// </summary>
public sealed class AuthenticationService(
    UserManager<StockpileIdentityUser> users,
    AppDbContext db,
    JwtTokenService tokens,
    IClock clock,
    ICurrentUser currentUser)
{
    /// <summary>
    /// Deliberately identical for an unknown email and a wrong password. Differing
    /// responses let an attacker enumerate valid accounts, and
    /// Login_withUnknownEmail_returns401_withTheSameMessageAsAWrongPassword asserts it.
    /// </summary>
    public static readonly Error InvalidCredentials =
        new DomainRuleError("auth.invalid_credentials", "Invalid email or password.");

    public async Task<Result<AuthTokens>> LoginAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        var identityUser = await users.FindByEmailAsync(email);
        if (identityUser is null)
            return InvalidCredentials;

        if (!await users.CheckPasswordAsync(identityUser, password))
            return InvalidCredentials;

        var domainUser = await db.Users
            .SingleOrDefaultAsync(u => u.Id == identityUser.Id, cancellationToken);

        if (domainUser is null || !domainUser.IsActive)
            return InvalidCredentials;

        return await IssueAsync(domainUser.Id, domainUser.Email, domainUser.DisplayName,
            domainUser.Role, replacing: null, cancellationToken);
    }

    /// <summary>
    /// Rotation: a used refresh token is immediately invalid, which is what makes a stolen
    /// one detectable rather than a permanent backdoor. The old row is revoked in the SAME
    /// SaveChanges that writes the new one, or Refresh_withAnAlreadyUsedRefreshToken_returns401
    /// races itself under a retry.
    /// </summary>
    public async Task<Result<AuthTokens>> RefreshAsync(
        string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return InvalidCredentials;

        var hash = JwtTokenService.Hash(rawToken);

        var existing = await db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is null || !existing.IsActive(clock.UtcNow))
            return InvalidCredentials;

        var domainUser = await db.Users
            .SingleOrDefaultAsync(u => u.Id == existing.UserId, cancellationToken);

        if (domainUser is null || !domainUser.IsActive)
            return InvalidCredentials;

        return await IssueAsync(domainUser.Id, domainUser.Email, domainUser.DisplayName,
            domainUser.Role, replacing: existing, cancellationToken);
    }

    public async Task<Result> LogoutAsync(
        string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return Result.Ok();   // nothing to revoke; logging out is idempotent

        var hash = JwtTokenService.Hash(rawToken);
        var existing = await db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (existing is not null && existing.RevokedAt is null)
        {
            existing.RevokedAt = clock.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result.Ok();
    }

    public async Task<Result<AuthenticatedUser>> MeAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not { } userId)
            return new NotFoundError("User", Guid.Empty);

        var domainUser = await db.Users
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return domainUser is null
            ? new NotFoundError("User", userId)
            : new AuthenticatedUser(
                domainUser.Id, domainUser.Email, domainUser.DisplayName, domainUser.Role.ToString());
    }

    private async Task<Result<AuthTokens>> IssueAsync(
        Guid userId,
        string email,
        string displayName,
        Domain.Enums.Role role,
        RefreshToken? replacing,
        CancellationToken cancellationToken)
    {
        var access = tokens.CreateAccessToken(userId, email, role);
        var (raw, hash, expiresAt) = tokens.CreateRefreshToken();

        var issued = new RefreshToken
        {
            UserId = userId,
            TokenHash = hash,
            ExpiresAt = expiresAt
        };

        db.RefreshTokens.Add(issued);

        if (replacing is not null)
        {
            replacing.RevokedAt = clock.UtcNow;
            replacing.ReplacedByTokenId = issued.Id;
        }

        await db.SaveChangesAsync(cancellationToken);

        return new AuthTokens(
            access.Value, raw, access.ExpiresAt,
            new AuthenticatedUser(userId, email, displayName, role.ToString()));
    }
}
