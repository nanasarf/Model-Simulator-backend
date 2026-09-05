using System.Text.Json;

namespace SimulationPlatform.Domain.Runtime;

public sealed record RoleAssignment(
    Guid Id, Guid SessionId, Guid TeamId, Guid UserId, Guid RoleDefinitionId,
    IReadOnlySet<string> CapabilityCodes, DateTimeOffset AssignedAt, DateTimeOffset? RevokedAt = null)
{
    public bool IsActive => RevokedAt is null;
}

public sealed record ActionSubmission(
    Guid Id, Guid SessionId, int RoundNumber, Guid TeamId, Guid UserId,
    Guid RoleAssignmentId, string ActionCode, JsonElement Payload, string IdempotencyKey,
    DateTimeOffset SubmittedAt, string? SubmittedPhase = null);

public sealed record SimulationEvent(
    Guid Id, Guid SessionId, int RoundNumber, Guid? ActorId, string Type,
    DateTimeOffset OccurredAt, JsonElement Data)
{
    public static SimulationEvent Create(Guid sessionId, int round, Guid? actorId, string type, DateTimeOffset at, object data) =>
        new(Guid.NewGuid(), sessionId, round, actorId, type, at, JsonSerializer.SerializeToElement(data));
}

public sealed record SimulationSnapshot(
    Guid SessionId, Guid TeamId, int RoundNumber, string ModelIdentifier,
    string ModelVersion, JsonElement State, DateTimeOffset CreatedAt);
