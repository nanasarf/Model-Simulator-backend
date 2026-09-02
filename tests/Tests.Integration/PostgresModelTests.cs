using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Infrastructure.Persistence;

namespace Tests.Integration;

public sealed class PostgresModelTests
{
    [Fact]
    public void Production_model_has_required_uniqueness_and_jsonb_mappings()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=unused;Password=unused").Options;
        using var db = new PlatformDbContext(options);
        var model = db.Model;
        var action = model.FindEntityType(typeof(ActionSubmissionRow))!;
        Assert.Contains(action.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual([nameof(ActionSubmissionRow.UserId), nameof(ActionSubmissionRow.IdempotencyKey)]));
        Assert.Equal("jsonb", action.FindProperty(nameof(ActionSubmissionRow.PayloadJson))!.GetColumnType());
        var execution = model.FindEntityType(typeof(ExecutionRow))!;
        Assert.Contains(execution.GetIndexes(), index => index.IsUnique);
    }
}
