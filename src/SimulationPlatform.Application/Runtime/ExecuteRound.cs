using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Simulations.Core.Contracts;
using System.Security.Cryptography;
using System.Text;

namespace SimulationPlatform.Application.Runtime;

public sealed record ExecuteRoundCommand(Guid SessionId, Guid TeamId, Guid ActorUserId, Guid ExecutionId, string TraceId);
public sealed record ExecuteRoundResult(Guid ExecutionId, int RoundNumber, IReadOnlyDictionary<string, decimal> Metrics);

public sealed class ExecuteRoundHandler(IRuntimeStore runtime, IRoundExecutionStore executions,
    IScenarioCatalog scenarios, ISimulationModelRegistry models, ITransactionRunner transactions, IClock clock,
    IAuditWriter audit)
{
    public ValueTask<ExecuteRoundResult> HandleAsync(ExecuteRoundCommand command, CancellationToken ct) =>
        transactions.ExecuteAsync(token => ExecuteCoreAsync(command, token), ct);

    private async ValueTask<ExecuteRoundResult> ExecuteCoreAsync(ExecuteRoundCommand command, CancellationToken ct)
    {
        var session = await runtime.FindSessionAsync(command.SessionId, ct)
            ?? throw new DomainException("session.not_found", "Session was not found.");
        if (session.Phase != SessionPhases.Locked) throw new DomainException("round.not_locked", "Only a locked round can execute.");
        var scenario = await scenarios.FindAsync(session.ScenarioVersionId, ct)
            ?? throw new DomainException("scenario.not_found", "Frozen scenario was not found.");
        if (!await executions.TryClaimAsync(command.ExecutionId, session.Id, command.TeamId, session.RoundNumber, clock.UtcNow, ct))
            throw new DomainException("round.already_executed", "This round already has an execution.");

        session.TransitionTo(SessionPhases.Simulation, scenario, command.ActorUserId, clock.UtcNow);
        var prior = await runtime.FindLatestSnapshotAsync(session.Id, command.TeamId, ct)
            ?? throw new DomainException("state.not_initialized", "Team state is not initialized.");
        var actions = await executions.GetRoundActionsAsync(session.Id, command.TeamId, session.RoundNumber, ct);
        var model = models.Resolve(session.ModelIdentifier, session.ModelVersion);
        var seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{session.Seed}:{command.TeamId:N}:{session.RoundNumber}"));
        var seed = BitConverter.ToInt32(seedBytes, 0);
        var result = await model.ExecuteRoundAsync(new(prior.State,
            actions.Select(x => new RoundAction(x.ActionCode, x.Payload)).ToArray(), seed, session.RoundNumber), ct);
        await executions.AddSnapshotAsync(new(session.Id, command.TeamId, session.RoundNumber, session.ModelIdentifier,
            session.ModelVersion, result.State.Clone(), clock.UtcNow), ct);
        session.Append("SimulationExecuted", command.ActorUserId, clock.UtcNow, new { command.ExecutionId, command.TeamId, result.Metrics });
        session.TransitionTo(SessionPhases.Results, scenario, command.ActorUserId, clock.UtcNow);
        await executions.CompleteExecutionAsync(command.ExecutionId, clock.UtcNow, ct);
        await audit.WriteAsync(command.ActorUserId, "Runtime.RoundExecuted", "Session", session.Id.ToString(),
            command.TraceId, clock.UtcNow, ct);
        await runtime.SaveSessionAsync(session, ct);
        return new(command.ExecutionId, session.RoundNumber, result.Metrics);
    }
}
