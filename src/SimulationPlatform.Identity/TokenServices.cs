using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SimulationPlatform.Identity;

public sealed class JwtOptions
{
    public const string Section = "Authentication:Jwt";
    public required string Issuer { get; init; }
    public required string Audience { get; init; }
    public required string SigningKey { get; init; }
    public TimeSpan AccessTokenLifetime { get; init; } = TimeSpan.FromMinutes(10);
    public TimeSpan RefreshTokenLifetime { get; init; } = TimeSpan.FromDays(14);
}

public sealed record IssuedTokens(string AccessToken, string RefreshToken, DateTimeOffset AccessTokenExpiresAt);

public interface ITokenService
{
    ValueTask<IssuedTokens> IssueAsync(ApplicationUser user, IReadOnlyCollection<string> roles, CancellationToken cancellationToken);
    ValueTask<IssuedTokens> RotateAsync(string refreshToken, CancellationToken cancellationToken);
    ValueTask RevokeAsync(string refreshToken, string reason, CancellationToken cancellationToken);
    ValueTask RevokeFamilyAsync(Guid userId, Guid familyId, string reason, CancellationToken cancellationToken);
}

public sealed class TokenService(IdentityDataContext db, UserManager<ApplicationUser> users,
    IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public async ValueTask<IssuedTokens> IssueAsync(ApplicationUser user, IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var family = Guid.NewGuid();
        return await IssueCoreAsync(user, roles, family, now, ct);
    }

    public async ValueTask<IssuedTokens> RotateAsync(string refreshToken, CancellationToken ct)
    {
        byte[] raw;
        try { raw = Convert.FromBase64String(refreshToken); }
        catch (FormatException error) { throw new SecurityTokenException("Refresh token is invalid.", error); }
        var hash = SHA256.HashData(raw);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var current = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct)
            ?? throw new SecurityTokenException("Refresh token is invalid.");
        var now = timeProvider.GetUtcNow();
        if (!current.IsUsable(now))
        {
            await RevokeFamilyRowsAsync(current.UserId, current.FamilyId, "refresh_token_reuse", now, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            throw new SecurityTokenException("Refresh token is expired, revoked, or reused.");
        }
        var user = await db.Users.SingleAsync(x => x.Id == current.UserId && x.IsActive, ct);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(current.SecurityStamp), Encoding.UTF8.GetBytes(user.SecurityStamp ?? "")))
            throw new SecurityTokenException("The account security stamp changed.");
        current.RevokedAt = now;
        current.RevocationReason = "rotated";
        var roles = await users.GetRolesAsync(user);
        var issued = await IssueCoreAsync(user, roles.ToArray(), current.FamilyId, now, ct, save: false);
        var replacement = db.ChangeTracker.Entries<RefreshToken>().Single(x => x.Entity.TokenHash.SequenceEqual(SHA256.HashData(Convert.FromBase64String(issued.RefreshToken)))).Entity;
        current.ReplacedByTokenId = replacement.Id;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return issued;
    }

    public async ValueTask RevokeAsync(string refreshToken, string reason, CancellationToken ct)
    {
        byte[] raw;
        try { raw = Convert.FromBase64String(refreshToken); }
        catch (FormatException) { return; }
        var hash = SHA256.HashData(raw);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is null) return;
        await RevokeFamilyAsync(token.UserId, token.FamilyId, reason, ct);
    }

    public async ValueTask RevokeFamilyAsync(Guid userId, Guid familyId, string reason, CancellationToken ct)
    {
        await RevokeFamilyRowsAsync(userId, familyId, reason, timeProvider.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
    }

    private async ValueTask<IssuedTokens> IssueCoreAsync(ApplicationUser user, IReadOnlyCollection<string> roles, Guid family, DateTimeOffset now, CancellationToken ct, bool save = true)
    {
        if (roles.Any(x => !PlatformRoles.All.Contains(x))) throw new InvalidOperationException("Unknown platform role.");
        var expires = now.Add(_options.AccessTokenLifetime);
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()), new("security_stamp", user.SecurityStamp ?? "") };
        claims.AddRange(roles.Select(x => new Claim(ClaimTypes.Role, x)));
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var jwt = new JwtSecurityToken(_options.Issuer, _options.Audience, claims, now.UtcDateTime, expires.UtcDateTime, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        var raw = RandomNumberGenerator.GetBytes(64);
        db.RefreshTokens.Add(new RefreshToken { Id = Guid.NewGuid(), UserId = user.Id, FamilyId = family, TokenHash = SHA256.HashData(raw), CreatedAt = now, ExpiresAt = now.Add(_options.RefreshTokenLifetime), SecurityStamp = user.SecurityStamp ?? "" });
        if (save) await db.SaveChangesAsync(ct);
        return new(new JwtSecurityTokenHandler().WriteToken(jwt), Convert.ToBase64String(raw), expires);
    }

    private async Task RevokeFamilyRowsAsync(Guid userId, Guid familyId, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens.Where(x => x.UserId == userId && x.FamilyId == familyId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var token in tokens) { token.RevokedAt = now; token.RevocationReason = reason; }
    }
}
