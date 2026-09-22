using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using SimulationPlatform.Application.Assessment;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class EfAssessmentCommentStore(PlatformDbContext db, IClock clock) : IAssessmentCommentStore
{
    public async ValueTask<AssessmentComment> AddAsync(Guid instructorId, Guid sessionId, string targetType, Guid targetId, string text, string idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) throw new DomainException("idempotency.required", "An idempotency key is required.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) throw new DomainException("assessment.comment_invalid", "Comment must contain 1–4000 characters.");
        var owned = await OwnsSession(instructorId, sessionId, ct);
        if (!owned) throw new DomainException("session.not_found", "Session was not found.");
        if (targetType is not ("Student" or "Role" or "Team" or "Session")) throw new DomainException("assessment.target_invalid", "Target type must be Student, Role, Team, or Session.");
        var requestHash = SHA256.HashData(Encoding.UTF8.GetBytes($"{sessionId:N}:{targetType}:{targetId:N}:{text.Trim()}"));
        var prior = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == instructorId && x.Operation == "AssessmentComment.Create" && x.Key == idempotencyKey, ct);
        if (prior is not null)
        {
            if (!CryptographicOperations.FixedTimeEquals(requestHash, prior.RequestHash)) throw new DomainException("idempotency.conflict", "Idempotency key was reused with different content.");
            var existing = await db.AssessmentComments.AsNoTracking().SingleAsync(x => x.Id == prior.ResultId, ct); return Map(existing);
        }
        var row = new AssessmentCommentRow { Id = Guid.NewGuid(), SessionId = sessionId, TargetType = targetType.Trim(), TargetId = targetId, AuthorUserId = instructorId, Text = text.Trim(), Version = 1, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
        db.AssessmentComments.Add(row);
        db.IdempotencyRecords.Add(new IdempotencyRow { Id = Guid.NewGuid(), UserId = instructorId, Operation = "AssessmentComment.Create", Key = idempotencyKey, RequestHash = requestHash, ResponseStatus = 201, ResultId = row.Id, CreatedAt = clock.UtcNow, ExpiresAt = clock.UtcNow.AddDays(7) });
        db.AuditRecords.Add(new AuditRow { Id = Guid.NewGuid(), ActorUserId = instructorId, Action = "AssessmentComment.Created", ResourceType = "AssessmentComment", ResourceId = row.Id.ToString(), TraceId = "assessment", MetadataJson = "{}", OccurredAt = clock.UtcNow });
        await db.SaveChangesAsync(ct); return Map(row);
    }
    public async ValueTask<AssessmentComment> UpdateAsync(Guid instructorId, Guid commentId, string text, long expectedVersion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000) throw new DomainException("assessment.comment_invalid", "Comment must contain 1–4000 characters.");
        var row = await db.AssessmentComments.SingleOrDefaultAsync(x => x.Id == commentId && x.AuthorUserId == instructorId, ct) ?? throw new DomainException("assessment.comment_not_found", "Comment was not found.");
        if (row.Version != expectedVersion) throw new DomainException("concurrency.conflict", "The comment changed; reload before editing.");
        row.Text = text.Trim(); row.Version++; row.UpdatedAt = clock.UtcNow;
        db.AuditRecords.Add(new AuditRow { Id = Guid.NewGuid(), ActorUserId = instructorId, Action = "AssessmentComment.Updated", ResourceType = "AssessmentComment", ResourceId = row.Id.ToString(), TraceId = "assessment", MetadataJson = "{}", OccurredAt = clock.UtcNow });
        await db.SaveChangesAsync(ct); return Map(row);
    }
    public async ValueTask<IReadOnlyList<AssessmentComment>> ListAsync(Guid instructorId, Guid sessionId, CancellationToken ct)
    {
        if (!await OwnsSession(instructorId, sessionId, ct)) throw new DomainException("session.not_found", "Session was not found.");
        return await db.AssessmentComments.AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.CreatedAt).Select(x => new AssessmentComment(x.Id, x.SessionId, x.TargetType, x.TargetId, x.AuthorUserId, x.Text, x.Version, x.CreatedAt, x.UpdatedAt)).ToArrayAsync(ct);
    }
    private async Task<bool> OwnsSession(Guid instructorId, Guid sessionId, CancellationToken ct) => await db.Sessions.Join(db.Classrooms, s => s.ClassroomId, c => c.Id, (s, c) => new { s, c.CourseId }).Join(db.Courses, x => x.CourseId, c => c.Id, (x, c) => new { x.s.Id, c.OwnerUserId }).AnyAsync(x => x.Id == sessionId && x.OwnerUserId == instructorId, ct);
    private static AssessmentComment Map(AssessmentCommentRow x) => new(x.Id, x.SessionId, x.TargetType, x.TargetId, x.AuthorUserId, x.Text, x.Version, x.CreatedAt, x.UpdatedAt);
}
