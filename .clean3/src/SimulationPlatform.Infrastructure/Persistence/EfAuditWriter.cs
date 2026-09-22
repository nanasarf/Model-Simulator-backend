using SimulationPlatform.Application.Abstractions;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class EfAuditWriter(PlatformDbContext db) : IAuditWriter
{
    public ValueTask WriteAsync(Guid? actor, string action, string resourceType, string resourceId,
        string traceId, DateTimeOffset at, CancellationToken ct)
    {
        db.AuditRecords.Add(new AuditRow { Id = Guid.NewGuid(), ActorUserId = actor, Action = action,
            ResourceType = resourceType, ResourceId = resourceId, TraceId = traceId,
            MetadataJson = "{}", OccurredAt = at });
        return ValueTask.CompletedTask;
    }
}
