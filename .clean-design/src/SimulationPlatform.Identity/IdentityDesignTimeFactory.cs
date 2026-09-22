using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SimulationPlatform.Identity;

public sealed class IdentityDesignTimeFactory : IDesignTimeDbContextFactory<IdentityDataContext>
{
    public IdentityDataContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<IdentityDataContext>()
        .UseNpgsql("Host=localhost;Database=simulation_platform;Username=design_time;Password=design_time").Options);
}
