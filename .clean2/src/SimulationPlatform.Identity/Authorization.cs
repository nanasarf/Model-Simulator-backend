using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace SimulationPlatform.Identity.Authorization;

public static class PlatformPolicies
{
    public const string Instructor = "Platform.Instructor";
    public const string Student = "Platform.Student";
    public const string Administrator = "Platform.Administrator";
    public const string ManageOwnedResource = "Resource.ManageOwned";
}

public interface IOwnedResource { Guid OwnerUserId { get; } }
public sealed class ManageOwnedResourceRequirement : IAuthorizationRequirement;

public sealed class ManageOwnedResourceHandler : AuthorizationHandler<ManageOwnedResourceRequirement, IOwnedResource>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context,
        ManageOwnedResourceRequirement requirement, IOwnedResource resource)
    {
        var subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        if (Guid.TryParse(subject, out var userId) &&
            (userId == resource.OwnerUserId || context.User.IsInRole(PlatformRoles.Administrator)))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
