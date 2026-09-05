using System.Text.Json;

namespace SimulationPlatform.Application.Classrooms;

public sealed record ScenarioDraftDocument(Guid Id, Guid SimulationDefinitionId, string Name, string Status,
    JsonElement Content, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IScenarioDraftStore
{
    ValueTask<ScenarioDraftDocument> CreateAsync(Guid ownerId, Guid definitionId, string name, JsonElement content, string idempotencyKey, CancellationToken ct);
    ValueTask<ScenarioDraftDocument> GetAsync(Guid ownerId, Guid draftId, CancellationToken ct);
    ValueTask<ScenarioDraftDocument> UpdateAsync(Guid ownerId, Guid draftId, string name, JsonElement content, long expectedVersion, CancellationToken ct);
    ValueTask<ScenarioDraftDocument> CloneAsync(Guid ownerId, Guid draftId, string name, string idempotencyKey, CancellationToken ct);
    ValueTask ArchiveAsync(Guid ownerId, Guid draftId, long expectedVersion, CancellationToken ct);
    ValueTask MarkPublishedAsync(Guid ownerId, Guid draftId, Guid scenarioVersionId, long expectedVersion, CancellationToken ct);
}
