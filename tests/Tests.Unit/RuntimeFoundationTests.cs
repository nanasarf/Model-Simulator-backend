using System.Text.Json;
using SimulationPlatform.Application.Actions;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Definitions;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Infrastructure.InMemory;
using SimulationPlatform.Simulations.Core.Contracts;
using SimulationPlatform.Simulations.Economics.SupplyDemand;

namespace Tests.Unit;

public sealed class RuntimeFoundationTests
{
    [Fact]
    public void Invalid_phase_transition_is_rejected()
    {
        var scenario = Scenario();
        var session = new SimulationSession(Guid.NewGuid(), scenario, 42, DateTimeOffset.UnixEpoch);
        var error = Assert.Throws<DomainException>(() => session.TransitionTo(SessionPhases.Results, scenario, Guid.NewGuid(), DateTimeOffset.UnixEpoch));
        Assert.Equal("round.invalid_transition", error.Code);
    }

    [Fact]
    public async Task Same_idempotent_action_returns_original_submission()
    {
        var store = new InMemoryRuntimeStore();
        var scenario = Scenario();
        var session = new SimulationSession(Guid.NewGuid(), scenario, 42, DateTimeOffset.UnixEpoch);
        session.TransitionTo(SessionPhases.Decision, scenario, Guid.NewGuid(), DateTimeOffset.UnixEpoch);
        var user = Guid.NewGuid(); var team = Guid.NewGuid(); var assignmentId = Guid.NewGuid();
        store.Scenarios[scenario.Id] = scenario; store.Sessions[session.Id] = session;
        store.Assignments.Add(new(assignmentId, session.Id, team, user, Guid.NewGuid(), new HashSet<string> { "CHANGE_PRODUCTION" }, DateTimeOffset.UnixEpoch));
        var model = new SupplyDemandModel();
        var state = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new { }), 42), default);
        store.Snapshots.Add(new(session.Id, team, 0, model.Descriptor.Identifier, model.Descriptor.Version, state, DateTimeOffset.UnixEpoch));
        var handler = new SubmitActionHandler(store, store, new SimulationModelRegistry([model]), new FixedClock());
        var command = new SubmitActionCommand(session.Id, team, user, assignmentId, "CHANGE_OUTPUT", JsonSerializer.SerializeToElement(new { direction = "increase" }), "retry-key");

        var first = await handler.HandleAsync(command, default);
        var retry = await handler.HandleAsync(command, default);

        Assert.Equal(first.Id, retry.Id);
        Assert.Single(store.Submissions);
    }

    [Fact]
    public async Task Supply_increase_reduces_price_and_increases_quantity()
    {
        var model = new SupplyDemandModel();
        var initial = await model.InitializeAsync(new(JsonSerializer.SerializeToElement(new { }), 1), default);
        var result = await model.ExecuteRoundAsync(new(initial, [new("CHANGE_OUTPUT", JsonSerializer.SerializeToElement(new { direction = "increase" }))], 1, 1), default);
        var state = result.State.Deserialize<SupplyDemandState>()!;
        Assert.True(state.Price < 60);
        Assert.True(state.Quantity > 40);
    }

    private static ScenarioVersion Scenario()
    {
        var action = new ActionDefinition(Guid.NewGuid(), "CHANGE_OUTPUT", "CHANGE_PRODUCTION", new HashSet<string> { SessionPhases.Decision });
        return new(Guid.NewGuid(), "Coffee Market", 1, "Economics.SupplyDemand", "1.0.0",
            [SessionPhases.Briefing, SessionPhases.Decision, SessionPhases.Locked, SessionPhases.Simulation, SessionPhases.Results],
            new Dictionary<string, IReadOnlySet<string>> {
                [SessionPhases.Briefing] = new HashSet<string> { SessionPhases.Decision },
                [SessionPhases.Decision] = new HashSet<string> { SessionPhases.Locked },
                [SessionPhases.Locked] = new HashSet<string> { SessionPhases.Simulation },
                [SessionPhases.Simulation] = new HashSet<string> { SessionPhases.Results } },
            new Dictionary<string, ActionDefinition> { [action.Code] = action });
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}
