using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Application.Rules;
using SimulationPlatform.Infrastructure.Persistence;
using SimulationPlatform.Simulations.Core.Authoring;
using SimulationPlatform.Simulations.Economics.Authoring;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Api;

public sealed record ScenarioProposalApprovalCommand(Guid ProposalId, Guid ActorUserId, long ExpectedVersion, string IdempotencyKey);
public sealed record ScenarioProposalApprovalResult(int StatusCode, string ResponseBody);
public interface IScenarioProposalApprovalService
{
    Task<ScenarioProposalApprovalResult> ApproveAsync(ScenarioProposalApprovalCommand command, CancellationToken cancellationToken);
}

public sealed class ScenarioProposalApprovalService(
    PlatformDbContext db,
    ScenarioBlueprintValidator validator,
    IAuthoringCapabilityCatalog catalogs,
    IScenarioRuleCompiler compiler,
    IScenarioBlueprintAdapterRegistry adapters,
    IScenarioDraftStore drafts,
    IClock clock,
    IIdempotencyRequestHasher hasher,
    IProposalCommandFaultInjector faults) : IScenarioProposalApprovalService
{
    public async Task<ScenarioProposalApprovalResult> ApproveAsync(ScenarioProposalApprovalCommand command, CancellationToken ct)
    {
        var hash = hasher.Hash("scenario_proposal.approve", command.ProposalId, command.ExpectedVersion);
        var existing = await db.IdempotencyRecords.Where(x => x.UserId == command.ActorUserId && x.Key == command.IdempotencyKey).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        if (existing is not null)
        {
            if (!existing.RequestHash.SequenceEqual(hash)) throw new DomainException("idempotency.conflict", "Idempotency key was used for a different request.");
            if (existing.CompletedAt is not null && existing.ResponseBody is not null) return new(existing.ResponseStatus, existing.ResponseBody);
            throw new DomainException("idempotency.in_progress", "An identical operation is already in progress.");
        }
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var row = await db.ScenarioProposals.SingleOrDefaultAsync(x => x.Id == command.ProposalId && x.OwnerInstructorUserId == command.ActorUserId, ct)
            ?? throw new DomainException("scenario_proposal.not_found", "Proposal was not found.");
        if (row.Status == "ConvertedToScenario") throw new DomainException("scenario_proposal.already_converted", "Proposal is already converted.");
        if (row.Status == "Rejected") throw new DomainException("scenario_proposal.immutable", "Proposal is immutable.");
        if (row.Version != command.ExpectedVersion) throw new DomainException("scenario_proposal.version_conflict", "Proposal changed; reload and retry.");
        var idem = new IdempotencyRow { Id = Guid.NewGuid(), UserId = command.ActorUserId, Operation = "scenario_proposal.approve", Key = command.IdempotencyKey, RequestHash = hash, ResponseStatus = 200, CreatedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddDays(1) };
        db.IdempotencyRecords.Add(idem);
        var blueprint = JsonSerializer.Deserialize<ScenarioBlueprint>(row.CurrentBlueprintJson)!;
        var report = validator.Validate(blueprint);
        if (!report.IsValid) throw new DomainException("scenario_proposal.validation_failed", "Proposal validation failed.");
        var catalog = catalogs.GetCatalog(blueprint.ModelIdentifier, blueprint.ModelVersion);
        var compiled = blueprint.Rules.Select(x => compiler.Compile(x, catalog, blueprint.LearningObjectives.Select(o => o.Code).ToHashSet())).ToList();
        if (compiled.Any(x => !x.IsValid) || !adapters.TryResolve(blueprint.ModelIdentifier, blueprint.ModelVersion, out var adapter)) throw new DomainException("scenario_blueprint.adapter_failed", "Proposal cannot be adapted.");
        var adapted = adapter.Adapt(blueprint, compiled.Select(x => x.CompiledRule!).ToList());
        if (!adapted.IsValid) throw new DomainException("scenario_blueprint.adapter_failed", "Proposal cannot be adapted.");
        var definition = await db.SimulationDefinitions.FirstOrDefaultAsync(x => x.OwnerUserId == command.ActorUserId && x.Name == blueprint.ModelIdentifier, ct);
        if (definition is null) { definition = new SimulationDefinitionRow { Id = Guid.NewGuid(), OwnerUserId = command.ActorUserId, Name = blueprint.ModelIdentifier }; db.SimulationDefinitions.Add(definition); }
        await faults.CheckpointAsync(ProposalCommandCheckpoint.AfterDefinitionResolved, ct);
        var draft = await drafts.CreateAsync(command.ActorUserId, definition.Id, blueprint.Title, JsonSerializer.SerializeToElement(adapted.Content), "proposal:" + command.ProposalId + ":" + command.IdempotencyKey, ct);
        await faults.CheckpointAsync(ProposalCommandCheckpoint.AfterDraftCreated, ct);
        row.LinkedDraftId = draft.Id; row.Status = "ConvertedToScenario"; row.Version++; row.UpdatedAt = clock.UtcNow;
        await faults.CheckpointAsync(ProposalCommandCheckpoint.AfterProposalMutated, ct);
        var now = clock.UtcNow;
        db.AuditRecords.Add(AuditFactory.Create(command.ActorUserId, "ScenarioProposalConvertedToDraft", "ScenarioProposal", command.ProposalId.ToString(), "proposal", now));
        await faults.CheckpointAsync(ProposalCommandCheckpoint.AfterAuditAdded, ct);
        var response = JsonSerializer.Serialize(new { proposalId = command.ProposalId, status = row.Status, scenarioDraftId = draft.Id, modelIdentifier = row.ModelIdentifier, modelVersion = row.ModelVersion });
        db.Outbox.Add(new OutboxMessage { Id = Guid.NewGuid(), Type = "ScenarioProposalConvertedToDraft", AggregateId = command.ProposalId, PayloadJson = response, OccurredAt = now, NextAttemptAt = now });
        await faults.CheckpointAsync(ProposalCommandCheckpoint.AfterOutboxAdded, ct);
        await faults.CheckpointAsync(ProposalCommandCheckpoint.BeforeIdempotencyCompleted, ct);
        idem.ResponseBody = response; idem.CompletedAt = now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(200, response);
    }
}
