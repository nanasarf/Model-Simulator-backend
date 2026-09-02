using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Definitions;

namespace SimulationPlatform.Domain.Runtime;

public sealed class SimulationSession
{
    private readonly List<SimulationEvent> _events = [];

    public Guid Id { get; }
    public Guid ScenarioVersionId { get; }
    public string ModelIdentifier { get; }
    public string ModelVersion { get; }
    public int Seed { get; }
    public string Phase { get; private set; }
    public int RoundNumber { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<SimulationEvent> Events => _events;

    public SimulationSession(Guid id, ScenarioVersion scenario, int seed, DateTimeOffset createdAt)
    {
        if (scenario.Phases.Count == 0) throw new DomainException("scenario.no_phases", "A scenario must define at least one phase.");
        Id = id;
        ScenarioVersionId = scenario.Id;
        ModelIdentifier = scenario.ModelIdentifier;
        ModelVersion = scenario.ModelVersion;
        Seed = seed;
        Phase = scenario.Phases[0];
        RoundNumber = 1;
        Append("SessionCreated", null, createdAt, new { scenario.Id, scenario.Version, seed });
    }

    private SimulationSession(Guid id, Guid scenarioVersionId, string modelIdentifier, string modelVersion,
        int seed, string phase, int roundNumber, long version, IEnumerable<SimulationEvent> events)
    {
        Id = id; ScenarioVersionId = scenarioVersionId; ModelIdentifier = modelIdentifier;
        ModelVersion = modelVersion; Seed = seed; Phase = phase; RoundNumber = roundNumber; Version = version;
        _events.AddRange(events);
    }

    public static SimulationSession Restore(Guid id, Guid scenarioVersionId, string modelIdentifier,
        string modelVersion, int seed, string phase, int roundNumber, long version,
        IEnumerable<SimulationEvent>? events = null) =>
        new(id, scenarioVersionId, modelIdentifier, modelVersion, seed, phase, roundNumber, version, events ?? []);

    public void TransitionTo(string target, ScenarioVersion scenario, Guid actorId, DateTimeOffset at)
    {
        if (!scenario.AllowedTransitions.TryGetValue(Phase, out var allowed) || !allowed.Contains(target))
            throw new DomainException("round.invalid_transition", $"Transition from {Phase} to {target} is not allowed.");

        var previous = Phase;
        Phase = target;
        Version++;
        Append("RoundPhaseChanged", actorId, at, new { previous, current = target, RoundNumber });
    }

    public void Append(string type, Guid? actorId, DateTimeOffset at, object data) =>
        _events.Add(SimulationEvent.Create(Id, RoundNumber, actorId, type, at, data));
}
