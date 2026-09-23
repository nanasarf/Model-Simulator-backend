using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace SimulationPlatform.Identity;

public sealed class IdentityDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDataContext>
{
    public IdentityDataContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("src/SimulationPlatform.Api/appsettings.json", optional: true)
            .AddJsonFile("src/SimulationPlatform.Api/appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connection = configuration.GetConnectionString("Platform")
            ?? throw new InvalidOperationException("Connection string 'Platform' was not configured.");
        return new IdentityDataContext(new DbContextOptionsBuilder<IdentityDataContext>().UseNpgsql(connection).Options);
    }
}
