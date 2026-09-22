namespace SimulationPlatform.Application.Assessment;

public sealed record AssessmentComment(Guid Id, Guid SessionId, string TargetType, Guid TargetId,
    Guid AuthorUserId, string Text, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IAssessmentCommentStore
{
    ValueTask<AssessmentComment> AddAsync(Guid instructorId, Guid sessionId, string targetType, Guid targetId, string text, string idempotencyKey, CancellationToken ct);
    ValueTask<AssessmentComment> UpdateAsync(Guid instructorId, Guid commentId, string text, long expectedVersion, CancellationToken ct);
    ValueTask<IReadOnlyList<AssessmentComment>> ListAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
}
