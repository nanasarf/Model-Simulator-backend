using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Tests.Integration;

/// <summary>Disposable PostgreSQL fixture for migration and acceptance verification.</summary>
public sealed class DisposablePostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("simulation_platform")
        .WithUsername("simulation_platform")
        .WithPassword("test-only-password")
        .Build();

    public string ConnectionString => container.GetConnectionString();

    public async Task InitializeAsync()
    {
        try
        {
            await container.StartAsync();
            await using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
                .UseNpgsql(ConnectionString).Options);
            await db.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Disposable PostgreSQL requires a running Docker daemon and postgres:16-alpine readiness.", ex);
        }
    }

    public async Task DisposeAsync() => await container.DisposeAsync();
}

public sealed class DisposablePostgresSmokeTests : IClassFixture<DisposablePostgresFixture>
{
    private readonly DisposablePostgresFixture fixture;
    public DisposablePostgresSmokeTests(DisposablePostgresFixture fixture) => this.fixture = fixture;

    [Fact]
    public async Task DisposablePostgres_AppliesAllMigrations()
    {
        await using var db = new PlatformDbContext(new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql(fixture.ConnectionString).Options);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.Contains("20260922073327_ClassroomAdmissionAndAiMvp1Persistence", applied);
        Assert.True(await db.ClassroomJoinCodes.CountAsync() == 0);
        Assert.True(await db.ScenarioProposals.CountAsync() == 0);
    }
}
