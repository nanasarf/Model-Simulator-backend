using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Definitions;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Infrastructure.InMemory;

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class SimulationModelRegistry(IEnumerable<ISimulationModel> models) : ISimulationModelRegistry
{
    private readonly Dictionary<(string, string), ISimulationModel> _models = models.ToDictionary(x => (x.Descriptor.Identifier, x.Descriptor.Version));
    public ISimulationModel Resolve(string identifier, string version) => _models.TryGetValue((identifier, version), out var model)
        ? model : throw new DomainException("model.not_found", $"Simulation model {identifier}:{version} is not registered.");
}

public sealed class InMemoryRuntimeStore : IRuntimeStore, IScenarioCatalog
{
    public Dictionary<Guid, SimulationSession> Sessions { get; } = [];
    public Dictionary<Guid, ScenarioVersion> Scenarios { get; } = [];
    public List<RoleAssignment> Assignments { get; } = [];
    public List<ActionSubmission> Submissions { get; } = [];
    public List<SimulationSnapshot> Snapshots { get; } = [];

    public ValueTask<SimulationSession?> FindSessionAsync(Guid id, CancellationToken ct) => ValueTask.FromResult(Sessions.GetValueOrDefault(id));
    public ValueTask<ScenarioVersion?> FindAsync(Guid id, CancellationToken ct) => ValueTask.FromResult(Scenarios.GetValueOrDefault(id));
    public ValueTask<RoleAssignment?> FindAssignmentAsync(Guid sessionId, Guid userId, Guid assignmentId, CancellationToken ct) =>
        ValueTask.FromResult(Assignments.SingleOrDefault(x => x.SessionId == sessionId && x.UserId == userId && x.Id == assignmentId && x.IsActive));
    public ValueTask<ActionSubmission?> FindSubmissionByIdempotencyKeyAsync(Guid userId, string key, CancellationToken ct) =>
        ValueTask.FromResult(Submissions.SingleOrDefault(x => x.UserId == userId && x.IdempotencyKey == key));
    public ValueTask<SimulationSnapshot?> FindLatestSnapshotAsync(Guid sessionId, Guid teamId, CancellationToken ct) =>
        ValueTask.FromResult(Snapshots.Where(x => x.SessionId == sessionId && x.TeamId == teamId).MaxBy(x => x.RoundNumber));
    public ValueTask AddSubmissionAsync(ActionSubmission submission, CancellationToken ct) { Submissions.Add(submission); return ValueTask.CompletedTask; }
    public ValueTask SaveSessionAsync(SimulationSession session, CancellationToken ct) { Sessions[session.Id] = session; return ValueTask.CompletedTask; }
}
