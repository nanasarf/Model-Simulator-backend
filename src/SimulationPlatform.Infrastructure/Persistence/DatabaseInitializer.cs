using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SimulationPlatform.Identity;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IServiceProvider services) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PlatformDbContext>().Database.MigrateAsync(ct);
        await scope.ServiceProvider.GetRequiredService<IdentityDataContext>().Database.MigrateAsync(ct);
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var name in PlatformRoles.All)
            if (!await roles.RoleExistsAsync(name))
            {
                var result = await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = name });
                if (!result.Succeeded) throw new InvalidOperationException($"Could not create platform role {name}.");
            }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
