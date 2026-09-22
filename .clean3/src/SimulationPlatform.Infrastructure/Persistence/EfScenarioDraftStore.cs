using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class EfScenarioDraftStore(PlatformDbContext db, IClock clock) : IScenarioDraftStore
{
    public ValueTask<ScenarioDraftDocument> CreateAsync(Guid owner, Guid definitionId, string name, JsonElement content, string key, CancellationToken ct) =>
        CreateCore(owner, definitionId, name, content, key, "ScenarioDraft.Create", ct);

    public async ValueTask<ScenarioDraftDocument> GetAsync(Guid owner, Guid id, CancellationToken ct) => Map(await Owned(owner, id, ct));

    public async ValueTask<ScenarioDraftDocument> UpdateAsync(Guid owner, Guid id, string name, JsonElement content, long expected, CancellationToken ct)
    {
        var row = await Owned(owner, id, ct); Editable(row); Concurrency(row, expected);
        row.Name = Required(name); row.ContentJson = content.GetRawText(); row.Version++; row.UpdatedAt = clock.UtcNow;
        Audit(owner, "ScenarioDraft.Updated", row.Id); await db.SaveChangesAsync(ct); return Map(row);
    }

    public async ValueTask<ScenarioDraftDocument> CloneAsync(Guid owner, Guid id, string name, string key, CancellationToken ct)
    {
        var source = await Owned(owner, id, ct);
        return await CreateCore(owner, source.SimulationDefinitionId, name, JsonDocument.Parse(source.ContentJson).RootElement.Clone(), key, "ScenarioDraft.Clone", ct);
    }

    public async ValueTask ArchiveAsync(Guid owner, Guid id, long expected, CancellationToken ct)
    {
        var row = await Owned(owner, id, ct); Concurrency(row, expected); row.Status = "Archived"; row.Version++; row.UpdatedAt = clock.UtcNow;
        Audit(owner, "ScenarioDraft.Archived", row.Id); Message("ScenarioDraftArchived", row.Id); await db.SaveChangesAsync(ct);
    }

    public async ValueTask MarkPublishedAsync(Guid owner, Guid id, Guid versionId, long expected, CancellationToken ct)
    {
        var row = await Owned(owner, id, ct); Editable(row); Concurrency(row, expected);
        row.Status = "Published"; row.PublishedScenarioVersionId = versionId; row.Version++; row.UpdatedAt = clock.UtcNow;
        Audit(owner, "ScenarioDraft.Published", row.Id); Message("ScenarioDraftPublished", row.Id); await db.SaveChangesAsync(ct);
    }

    private async ValueTask<ScenarioDraftDocument> CreateCore(Guid owner, Guid definitionId, string name, JsonElement content, string key, string operation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new DomainException("idempotency.required", "An idempotency key is required.");
        if (!await db.SimulationDefinitions.AnyAsync(x => x.Id == definitionId && x.OwnerUserId == owner, ct))
            throw new DomainException("definition.not_found", "Simulation definition was not found.");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{definitionId:N}:{name}:{content.GetRawText()}"));
        var prior = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.Operation == operation && x.Key == key, ct);
        if (prior is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(hash, prior.RequestHash)) throw new DomainException("idempotency.conflict", "Idempotency key was reused with different content.");
            return Map(await Owned(owner, prior.ResultId!.Value, ct));
        }
        var now = clock.UtcNow; var row = new ScenarioDraftRow { Id = Guid.NewGuid(), OwnerUserId = owner,
            SimulationDefinitionId = definitionId, Name = Required(name), Status = "Draft", ContentJson = content.GetRawText(),
            Version = 1, CreatedAt = now, UpdatedAt = now };
        db.ScenarioDrafts.Add(row); db.IdempotencyRecords.Add(new IdempotencyRow { Id = Guid.NewGuid(), UserId = owner,
            Operation = operation, Key = key, RequestHash = hash, ResponseStatus = 201, ResultId = row.Id,
            CreatedAt = now, ExpiresAt = now.AddDays(7) });
        Audit(owner, operation, row.Id); Message("ScenarioDraftChanged", row.Id); await db.SaveChangesAsync(ct); return Map(row);
    }

    private async Task<ScenarioDraftRow> Owned(Guid owner, Guid id, CancellationToken ct) =>
        await db.ScenarioDrafts.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == owner, ct)
        ?? throw new DomainException("scenario_draft.not_found", "Scenario draft was not found.");
    private static void Editable(ScenarioDraftRow row)
    {
        if (row.Status != "Draft") throw new DomainException("scenario_draft.immutable", "Published or archived drafts cannot be edited; clone to create a new draft.");
    }
    private static void Concurrency(ScenarioDraftRow row, long expected) { if (row.Version != expected) throw new DomainException("concurrency.conflict", "The draft has changed; reload before editing."); }
    private static string Required(string value) => !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new DomainException("validation.required", "Name is required.");
    private static ScenarioDraftDocument Map(ScenarioDraftRow x) => new(x.Id, x.SimulationDefinitionId, x.Name, x.Status,
        JsonDocument.Parse(x.ContentJson).RootElement.Clone(), x.Version, x.CreatedAt, x.UpdatedAt);
    private void Audit(Guid actor, string action, Guid id) => db.AuditRecords.Add(new AuditRow { Id = Guid.NewGuid(), ActorUserId = actor,
        Action = action, ResourceType = "ScenarioDraft", ResourceId = id.ToString(), TraceId = "scenario-authoring", MetadataJson = "{}", OccurredAt = clock.UtcNow });
    private void Message(string type, Guid id) => db.Outbox.Add(new OutboxMessage { Id = Guid.NewGuid(), Type = type, AggregateId = id,
        PayloadJson = JsonSerializer.Serialize(new { scenarioDraftId = id }), OccurredAt = clock.UtcNow, NextAttemptAt = clock.UtcNow });
}
