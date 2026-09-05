using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Definitions;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Infrastructure.InMemory;

public sealed class InMemoryRuntimeStore : IRuntimeStore, IScenarioCatalog, ITransactionRunner
{
    public Dictionary<Guid, SimulationSession> Sessions { get; } = [];
    public Dictionary<Guid, ScenarioVersion> Scenarios { get; } = [];
    public List<RoleAssignment> Assignments { get; } = [];
    public List<ActionSubmission> Submissions { get; } = [];
    public List<SimulationSnapshot> Snapshots { get; } = [];

    public ValueTask<SimulationSession?> FindSessionAsync(Guid id, CancellationToken ct) => ValueTask.FromResult(Sessions.GetValueOrDefault(id));
    public ValueTask<ScenarioVersion?> FindAsync(Guid id, CancellationToken ct) => ValueTask.FromResult(Scenarios.GetValueOrDefault(id));
    public ValueTask<ScenarioVersion?> FindForSessionAsync(Guid sessionId, CancellationToken ct) =>
        ValueTask.FromResult(Sessions.TryGetValue(sessionId, out var session) ? Scenarios.GetValueOrDefault(session.ScenarioVersionId) : null);
    public ValueTask<RoleAssignment?> FindAssignmentAsync(Guid sessionId, Guid userId, Guid assignmentId, CancellationToken ct) =>
        ValueTask.FromResult(Assignments.SingleOrDefault(x => x.SessionId == sessionId && x.UserId == userId && x.Id == assignmentId && x.IsActive));
    public ValueTask<ActionSubmission?> FindSubmissionByIdempotencyKeyAsync(Guid userId, string key, CancellationToken ct) =>
        ValueTask.FromResult(Submissions.SingleOrDefault(x => x.UserId == userId && x.IdempotencyKey == key));
    public ValueTask<SimulationSnapshot?> FindLatestSnapshotAsync(Guid sessionId, Guid teamId, CancellationToken ct) =>
        ValueTask.FromResult(Snapshots.Where(x => x.SessionId == sessionId && x.TeamId == teamId).MaxBy(x => x.RoundNumber));
    public ValueTask AddSubmissionAsync(ActionSubmission submission, CancellationToken ct) { Submissions.Add(submission); return ValueTask.CompletedTask; }
    public ValueTask<int> CountSubmissionsAsync(Guid sessionId, int round, Guid assignmentId, string actionCode, CancellationToken ct) =>
        ValueTask.FromResult(Submissions.Count(x => x.SessionId == sessionId && x.RoundNumber == round &&
            x.RoleAssignmentId == assignmentId && x.ActionCode == actionCode));
    public ValueTask SaveSessionAsync(SimulationSession session, CancellationToken ct) { Sessions[session.Id] = session; return ValueTask.CompletedTask; }
    public ValueTask<T> ExecuteAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken ct) => operation(ct);
}

public sealed class AllowAllActionRules : IActionRuleEvaluator
{
    public ValueTask<bool> IsAllowedAsync(ActionRuleContext context, CancellationToken ct) => ValueTask.FromResult(true);
}
