using Microsoft.AspNetCore.Identity;

namespace SimulationPlatform.Identity;

public static class PlatformRoles
{
    public const string Administrator = "PlatformAdministrator";
    public const string Instructor = "Instructor";
    public const string Student = "Student";
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Administrator, Instructor, Student };
}

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ApplicationRole : IdentityRole<Guid>;

public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public required byte[] TokenHash { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public string? RevocationReason { get; set; }
    public required string SecurityStamp { get; set; }
    public bool IsUsable(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
