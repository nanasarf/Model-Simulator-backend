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

public interface ITransactionRunner
{
    ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken);
}

public interface IRoundExecutionStore
{
    ValueTask<bool> TryClaimAsync(Guid executionId, Guid sessionId, Guid teamId, int roundNumber, DateTimeOffset at, CancellationToken cancellationToken);
    ValueTask<IReadOnlyList<ActionSubmission>> GetRoundActionsAsync(Guid sessionId, Guid teamId, int roundNumber, CancellationToken cancellationToken);
    ValueTask AddSnapshotAsync(SimulationSnapshot snapshot, CancellationToken cancellationToken);
    ValueTask CompleteExecutionAsync(Guid executionId, DateTimeOffset at, CancellationToken cancellationToken);
}

public sealed record ActionRuleContext(Guid SessionId, string Phase, Guid TeamId,
    string ActionCode, IReadOnlySet<string> Capabilities);

public interface IActionRuleEvaluator
{
    ValueTask<bool> IsAllowedAsync(ActionRuleContext context, CancellationToken cancellationToken);
}

public interface IAuditWriter
{
    ValueTask WriteAsync(Guid? actorUserId, string action, string resourceType, string resourceId,
        string traceId, DateTimeOffset occurredAt, CancellationToken cancellationToken);
}
