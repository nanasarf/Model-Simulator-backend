using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Application.Abstractions;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class EfTransactionRunner(PlatformDbContext db) : ITransactionRunner
{
    public async ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var result = await operation(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        });
    }
}
