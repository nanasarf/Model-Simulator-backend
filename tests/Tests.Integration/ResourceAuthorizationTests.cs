using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SimulationPlatform.Identity;
using SimulationPlatform.Identity.Authorization;

namespace Tests.Integration;

public sealed class ResourceAuthorizationTests
{
    [Fact]
    public async Task Simulation_role_name_does_not_grant_platform_ownership()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, "President")], "test"));
        var requirement = new ManageOwnedResourceRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, new Resource(Guid.NewGuid()));
        await new ManageOwnedResourceHandler().HandleAsync(context);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Administrator_may_manage_resource_without_being_owner()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, PlatformRoles.Administrator)], "test"));
        var requirement = new ManageOwnedResourceRequirement();
        var context = new AuthorizationHandlerContext([requirement], principal, new Resource(Guid.NewGuid()));
        await new ManageOwnedResourceHandler().HandleAsync(context);
        Assert.True(context.HasSucceeded);
    }

    private sealed record Resource(Guid OwnerUserId) : IOwnedResource;
}
