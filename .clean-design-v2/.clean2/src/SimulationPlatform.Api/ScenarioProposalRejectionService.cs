using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Infrastructure.Persistence;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Api;

public sealed record ScenarioProposalRejectionCommand(Guid ProposalId, Guid ActorUserId, long ExpectedVersion, string IdempotencyKey);
public sealed record ScenarioProposalRejectionResult(int StatusCode, string ResponseBody);
public interface IScenarioProposalRejectionService
{
    Task<ScenarioProposalRejectionResult> RejectAsync(ScenarioProposalRejectionCommand command, CancellationToken cancellationToken);
}

public sealed class ScenarioProposalRejectionService(
    PlatformDbContext db,
    IClock clock,
    IIdempotencyRequestHasher hasher,
    IProposalCommandFaultInjector faults) : IScenarioProposalRejectionService
{
    public async Task<ScenarioProposalRejectionResult> RejectAsync(ScenarioProposalRejectionCommand command, CancellationToken ct)
    {
        var hash = hasher.Hash("scenario_proposal.reject", command.ProposalId, command.ExpectedVersion);
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
        if (row.Version != command.ExpectedVersion) throw new DomainException("scenario_proposal.version_conflict", "Proposal changed; reload and retry.");
        if (row.Status is "ConvertedToScenario" or "Rejected") throw new DomainException("scenario_proposal.immutable", "Proposal is immutable.");
        var idem = new IdempotencyRow { Id = Guid.NewGuid(), UserId = command.ActorUserId, Operation = "scenario_proposal.reject", Key = command.IdempotencyKey, RequestHash = hash, ResponseStatus = 200, CreatedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddDays(1) };
        db.IdempotencyRecords.Add(idem);
        row.Status = "Rejected"; row.Version++; row.UpdatedAt = clock.UtcNow;
        await faults.CheckpointAsync(ProposalCommandCheckpoint.AfterProposalMutated, ct);
        var now = clock.UtcNow; var response = JsonSerializer.Serialize(new { proposalId = command.ProposalId, status = row.Status });
        db.AuditRecords.Add(AuditFactory.Create(command.ActorUserId, "ScenarioProposalRejected", "ScenarioProposal", command.ProposalId.ToString(), "proposal", now));
        await faults.CheckpointAsync(ProposalCommandCheckpoint.AfterAuditAdded, ct);
        db.Outbox.Add(new OutboxMessage { Id = Guid.NewGuid(), Type = "ScenarioProposalRejected", AggregateId = command.ProposalId, PayloadJson = response, OccurredAt = now, NextAttemptAt = now });
        await faults.CheckpointAsync(ProposalCommandCheckpoint.AfterOutboxAdded, ct);
        await faults.CheckpointAsync(ProposalCommandCheckpoint.BeforeIdempotencyCompleted, ct);
        idem.ResponseBody = response; idem.CompletedAt = now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(200, response);
    }
}
