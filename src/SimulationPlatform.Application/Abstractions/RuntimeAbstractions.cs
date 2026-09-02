using SimulationPlatform.Domain.Runtime;

namespace SimulationPlatform.Application.Abstractions;

public interface IRuntimeStore
{
    ValueTask<SimulationSession?> FindSessionAsync(Guid id, CancellationToken cancellationToken);
    ValueTask<RoleAssignment?> FindAssignmentAsync(Guid sessionId, Guid userId, Guid assignmentId, CancellationToken cancellationToken);
    ValueTask<ActionSubmission?> FindSubmissionByIdempotencyKeyAsync(Guid userId, string key, CancellationToken cancellationToken);
    ValueTask<SimulationSnapshot?> FindLatestSnapshotAsync(Guid sessionId, Guid teamId, CancellationToken cancellationToken);
    ValueTask AddSubmissionAsync(ActionSubmission submission, CancellationToken cancellationToken);
    ValueTask SaveSessionAsync(SimulationSession session, CancellationToken cancellationToken);
}

public interface IScenarioCatalog
{
    ValueTask<Domain.Definitions.ScenarioVersion?> FindAsync(Guid versionId, CancellationToken cancellationToken);
}
