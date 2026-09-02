using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Domain.Definitions;
using SimulationPlatform.Domain.Runtime;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class EfRuntimeStore(PlatformDbContext db) : IRuntimeStore, IScenarioCatalog, IRoundExecutionStore
{
    public async ValueTask<SimulationSession?> FindSessionAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Sessions.SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : SimulationSession.Restore(row.Id, row.ScenarioVersionId, row.ModelIdentifier,
            row.ModelVersion, row.Seed, row.Phase, row.RoundNumber, row.Version);
    }

    public async ValueTask<ScenarioVersion?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await db.ScenarioVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : JsonSerializer.Deserialize<ScenarioVersion>(row.ConfigurationJson);
    }

    public async ValueTask<RoleAssignment?> FindAssignmentAsync(Guid sessionId, Guid userId, Guid assignmentId, CancellationToken ct)
    {
        var row = await db.RoleAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == userId && x.Id == assignmentId && x.RevokedAt == null, ct);
        return row is null ? null : new(row.Id, row.SessionId, row.TeamId, row.UserId, row.RoleDefinitionId,
            JsonSerializer.Deserialize<HashSet<string>>(row.CapabilitiesJson) ?? [], row.AssignedAt, row.RevokedAt);
    }

    public async ValueTask<ActionSubmission?> FindSubmissionByIdempotencyKeyAsync(Guid userId, string key, CancellationToken ct)
    {
        var row = await db.ActionSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == key, ct);
        return row is null ? null : new(row.Id, row.SessionId, row.RoundNumber, row.TeamId, row.UserId,
            row.RoleAssignmentId, row.ActionCode, JsonDocument.Parse(row.PayloadJson).RootElement.Clone(), row.IdempotencyKey, row.SubmittedAt);
    }

    public async ValueTask<SimulationSnapshot?> FindLatestSnapshotAsync(Guid sessionId, Guid teamId, CancellationToken ct)
    {
        var row = await db.Snapshots.AsNoTracking().Where(x => x.SessionId == sessionId && x.TeamId == teamId)
            .OrderByDescending(x => x.RoundNumber).FirstOrDefaultAsync(ct);
        return row is null ? null : new(row.SessionId, row.TeamId, row.RoundNumber, row.ModelIdentifier,
            row.ModelVersion, JsonDocument.Parse(row.StateJson).RootElement.Clone(), row.CreatedAt);
    }

    public ValueTask AddSubmissionAsync(ActionSubmission x, CancellationToken ct)
    {
        db.ActionSubmissions.Add(new ActionSubmissionRow { Id = x.Id, SessionId = x.SessionId, RoundNumber = x.RoundNumber,
            TeamId = x.TeamId, UserId = x.UserId, RoleAssignmentId = x.RoleAssignmentId, ActionCode = x.ActionCode,
            PayloadJson = x.Payload.GetRawText(), IdempotencyKey = x.IdempotencyKey, SubmittedAt = x.SubmittedAt });
        db.Outbox.Add(new OutboxMessage { Id = Guid.NewGuid(), Type = "ActionSubmissionStatusChanged", AggregateId = x.SessionId,
            PayloadJson = JsonSerializer.Serialize(new { x.Id, x.SessionId, x.TeamId, x.RoundNumber }), OccurredAt = x.SubmittedAt, NextAttemptAt = x.SubmittedAt });
        return ValueTask.CompletedTask;
    }

    public async ValueTask SaveSessionAsync(SimulationSession session, CancellationToken ct)
    {
        var row = await db.Sessions.SingleAsync(x => x.Id == session.Id, ct);
        row.Phase = session.Phase; row.RoundNumber = session.RoundNumber; row.Version = session.Version;
        var existingIds = await db.Events.Where(x => x.SessionId == session.Id).Select(x => x.Id).ToHashSetAsync(ct);
        var sequence = await db.Events.Where(x => x.SessionId == session.Id).Select(x => (long?)x.Sequence).MaxAsync(ct) ?? 0;
        foreach (var item in session.Events.Where(x => !existingIds.Contains(x.Id)))
            db.Events.Add(new EventRow { Id = item.Id, SessionId = item.SessionId, Sequence = ++sequence, RoundNumber = item.RoundNumber,
                ActorId = item.ActorId, Type = item.Type, DataJson = item.Data.GetRawText(), OccurredAt = item.OccurredAt });
    }

    public async ValueTask<bool> TryClaimAsync(Guid executionId, Guid sessionId, Guid teamId, int round, DateTimeOffset at, CancellationToken ct)
    {
        if (await db.Executions.AnyAsync(x => x.SessionId == sessionId && x.TeamId == teamId && x.RoundNumber == round, ct)) return false;
        db.Executions.Add(new ExecutionRow { Id = executionId, SessionId = sessionId, TeamId = teamId, RoundNumber = round, Status = "Running", StartedAt = at });
        return true;
    }

    public async ValueTask<IReadOnlyList<ActionSubmission>> GetRoundActionsAsync(Guid sessionId, Guid teamId, int round, CancellationToken ct)
    {
        var rows = await db.ActionSubmissions.AsNoTracking().Where(x => x.SessionId == sessionId && x.TeamId == teamId && x.RoundNumber == round).ToListAsync(ct);
        return rows.Select(x => new ActionSubmission(x.Id, x.SessionId, x.RoundNumber, x.TeamId, x.UserId, x.RoleAssignmentId,
            x.ActionCode, JsonDocument.Parse(x.PayloadJson).RootElement.Clone(), x.IdempotencyKey, x.SubmittedAt)).ToArray();
    }

    public ValueTask AddSnapshotAsync(SimulationSnapshot x, CancellationToken ct)
    {
        db.Snapshots.Add(new SnapshotRow { Id = Guid.NewGuid(), SessionId = x.SessionId, TeamId = x.TeamId,
            RoundNumber = x.RoundNumber, ModelIdentifier = x.ModelIdentifier, ModelVersion = x.ModelVersion,
            StateJson = x.State.GetRawText(), CreatedAt = x.CreatedAt });
        db.Outbox.Add(new OutboxMessage { Id = Guid.NewGuid(), Type = "ResultsAvailable", AggregateId = x.SessionId,
            PayloadJson = JsonSerializer.Serialize(new { x.SessionId, x.TeamId, x.RoundNumber }), OccurredAt = x.CreatedAt, NextAttemptAt = x.CreatedAt });
        return ValueTask.CompletedTask;
    }

    public async ValueTask CompleteExecutionAsync(Guid executionId, DateTimeOffset at, CancellationToken ct)
    {
        var row = await db.Executions.SingleAsync(x => x.Id == executionId && x.Status == "Running", ct);
        row.Status = "Completed"; row.CompletedAt = at;
    }
}
