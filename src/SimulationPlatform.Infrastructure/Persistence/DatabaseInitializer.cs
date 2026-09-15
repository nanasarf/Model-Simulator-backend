using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimulationPlatform.Identity;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class DatabaseInitializer(IServiceProvider services, IHostEnvironment environment, ILogger<DatabaseInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            logger.LogInformation("Starting database initialization...");
            await using var scope = services.CreateAsyncScope();

            logger.LogInformation("Applying PlatformDbContext migrations...");
            var platformDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            await platformDb.Database.MigrateAsync(ct);
            logger.LogInformation("PlatformDbContext migrations applied successfully.");

            logger.LogInformation("Applying IdentityDataContext migrations...");
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityDataContext>();
            await identityDb.Database.MigrateAsync(ct);
            logger.LogInformation("IdentityDataContext migrations applied successfully.");

            logger.LogInformation("Creating platform roles...");
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            foreach (var name in PlatformRoles.All)
                if (!await roles.RoleExistsAsync(name))
                {
                    var result = await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = name });
                    if (!result.Succeeded) throw new InvalidOperationException($"Could not create platform role {name}.");
                    logger.LogInformation("Created role: {RoleName}", name);
                }
            logger.LogInformation("Database initialization completed successfully.");
        }
        catch (Exception ex) when (environment.IsDevelopment())
        {
            logger.LogError(ex, "Database initialization failed. The API will remain available for health and Swagger, but database-backed endpoints require PostgreSQL.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database initialization failed in production environment.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
