using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SimulationPlatform.Infrastructure.Persistence;

namespace SimulationPlatform.Infrastructure.Outbox;

public interface IIntegrationEventPublisher
{
    ValueTask PublishAsync(Guid messageId, string type, string payloadJson, CancellationToken cancellationToken);
}

public sealed class OutboxDispatcher(IDbContextFactory<PlatformDbContext> dbFactory,
    IIntegrationEventPublisher publisher, TimeProvider timeProvider, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = await DispatchBatchAsync(stoppingToken);
            if (processed == 0) await Task.Delay(TimeSpan.FromSeconds(1), timeProvider, stoppingToken);
        }
    }

    public async Task<int> DispatchBatchAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = timeProvider.GetUtcNow();
        var claimId = Guid.NewGuid();
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var claimed = await db.Outbox.FromSqlInterpolated($@"SELECT * FROM integration.outbox_messages
                WHERE ""ProcessedAt"" IS NULL AND ""NextAttemptAt"" <= {now}
                AND (""ClaimedUntil"" IS NULL OR ""ClaimedUntil"" < {now})
                ORDER BY ""OccurredAt"" FOR UPDATE SKIP LOCKED LIMIT 50").ToListAsync(ct);
            foreach (var message in claimed) { message.ClaimId = claimId; message.ClaimedUntil = now.AddMinutes(2); }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        var messages = await db.Outbox.Where(x => x.ClaimId == claimId).OrderBy(x => x.OccurredAt).ToListAsync(ct);
        foreach (var message in messages)
        {
            try { await publisher.PublishAsync(message.Id, message.Type, message.PayloadJson, ct); message.ProcessedAt = timeProvider.GetUtcNow(); message.ClaimId = null; message.ClaimedUntil = null; }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                message.Attempts++;
                message.LastError = error.GetType().Name;
                message.NextAttemptAt = now.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(message.Attempts, 8))));
                message.ClaimId = null; message.ClaimedUntil = null;
                logger.LogWarning("Outbox publication {MessageId} failed on attempt {Attempt}", message.Id, message.Attempts);
            }
        }
        await db.SaveChangesAsync(ct);
        return messages.Count;
    }
}
