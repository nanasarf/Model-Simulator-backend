using System.Text.Json;
using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Application.Actions;

public sealed record SubmitActionCommand(
    Guid SessionId, Guid TeamId, Guid UserId, Guid RoleAssignmentId,
    string ActionCode, JsonElement Payload, string IdempotencyKey);

public sealed class SubmitActionHandler(
    IRuntimeStore store, IScenarioCatalog scenarios, ISimulationModelRegistry models, IClock clock,
    ITransactionRunner transactions, IActionRuleEvaluator rules)
{
    public ValueTask<ActionSubmission> HandleAsync(SubmitActionCommand command, CancellationToken cancellationToken) =>
        transactions.ExecuteAsync(ct => HandleCoreAsync(command, ct), cancellationToken);

    private async ValueTask<ActionSubmission> HandleCoreAsync(SubmitActionCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new DomainException("idempotency.required", "An idempotency key is required.");

        var prior = await store.FindSubmissionByIdempotencyKeyAsync(command.UserId, command.IdempotencyKey, cancellationToken);
        if (prior is not null)
        {
            if (SameRequest(prior, command)) return prior;
            throw new DomainException("idempotency.conflict", "The idempotency key was used for a different request.");
        }

        var session = await store.FindSessionAsync(command.SessionId, cancellationToken)
            ?? throw new DomainException("session.not_found", "Session was not found.");
        var scenario = await scenarios.FindForSessionAsync(session.Id, cancellationToken)
            ?? throw new DomainException("scenario.not_found", "The frozen scenario version was not found.");
        var assignment = await store.FindAssignmentAsync(command.SessionId, command.UserId, command.RoleAssignmentId, cancellationToken)
            ?? throw new DomainException("assignment.not_found", "No active role assignment was found.");

        if (assignment.TeamId != command.TeamId) throw new DomainException("team.forbidden", "The assignment does not belong to this team.");
        if (!scenario.Actions.TryGetValue(command.ActionCode, out var action)) throw new DomainException("action.unknown", "Unknown action.");
        if (!assignment.CapabilityCodes.Contains(action.RequiredCapability)) throw new DomainException("capability.denied", "The role lacks the required capability.");
        if (!action.AvailablePhases.Contains(session.Phase)) throw new DomainException("action.phase_denied", "The action is unavailable in the current phase.");
        var submissionCount = await store.CountSubmissionsAsync(session.Id, session.RoundNumber,
            assignment.Id, command.ActionCode, cancellationToken);
        if (!await rules.IsAllowedAsync(new(session.Id, session.Phase, command.TeamId,
            command.ActionCode, assignment.CapabilityCodes, submissionCount), cancellationToken))
            throw new DomainException("rule.denied", "The configured rules deny this action.");

        var snapshot = await store.FindLatestSnapshotAsync(command.SessionId, command.TeamId, cancellationToken)
            ?? throw new DomainException("state.not_initialized", "The team state has not been initialized.");
        var model = models.Resolve(session.ModelIdentifier, session.ModelVersion);
        var validation = await model.ValidateActionAsync(new(snapshot.State, command.ActionCode, command.Payload), cancellationToken);
        if (!validation.IsValid) throw new DomainException(validation.ErrorCode ?? "action.invalid", validation.Message ?? "The model rejected the action.");

        var submission = new ActionSubmission(Guid.NewGuid(), command.SessionId, session.RoundNumber, command.TeamId,
            command.UserId, command.RoleAssignmentId, command.ActionCode, command.Payload.Clone(), command.IdempotencyKey, clock.UtcNow, session.Phase);
        await store.AddSubmissionAsync(submission, cancellationToken);
        session.Append("ActionSubmitted", command.UserId, clock.UtcNow, new { submission.Id, command.TeamId, command.ActionCode });
        await store.SaveSessionAsync(session, cancellationToken);
        return submission;
    }

    private static bool SameRequest(ActionSubmission prior, SubmitActionCommand command) =>
        prior.SessionId == command.SessionId && prior.TeamId == command.TeamId && prior.RoleAssignmentId == command.RoleAssignmentId &&
        prior.ActionCode == command.ActionCode && JsonElement.DeepEquals(prior.Payload, command.Payload);
}
