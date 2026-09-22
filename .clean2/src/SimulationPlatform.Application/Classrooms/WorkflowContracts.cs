using System.Text.Json;

namespace SimulationPlatform.Application.Classrooms;

public sealed record RoleManifest(string Code, string Name, int MinimumParticipants, int MaximumParticipants,
    HashSet<string> Capabilities);
public sealed record ActionManifest(string Code, string RequiredCapability, HashSet<string> AvailablePhases, JsonElement? Constraints = null);
public sealed record RuleManifest(Guid Id, int Priority, string Effect, JsonElement Condition);
public sealed record ScenarioManifest(string ModelIdentifier, string ModelVersion, int ConfigurationVersion,
    JsonElement ModelConfiguration, List<string> Phases,
    Dictionary<string, HashSet<string>> AllowedTransitions,
    List<RoleManifest> Roles, List<ActionManifest> Actions, List<RuleManifest> Rules,
    HashSet<string>? ReadinessRequiredPhases = null, int? MaximumRounds = null, JsonElement? Presentation = null);

public sealed record RecoveryRoleAssignment(Guid AssignmentId, Guid TeamId, string RoleCode, IReadOnlySet<string> Capabilities);
public sealed record RecoveryActionDefinition(string Code, string RequiredCapability, IReadOnlySet<string> AvailablePhases, JsonElement? Constraints);
public sealed record RecoverySubmission(Guid SubmissionId, Guid RoleAssignmentId, string ActionCode, string Status,
    DateTimeOffset? SubmittedAt, JsonElement? Payload);
public sealed record SessionRecoveryView(Guid SessionId, string Status, string Phase, int RoundNumber,
    string ModelIdentifier, string ModelVersion, Guid? TeamId, IReadOnlyList<RecoveryRoleAssignment> RoleAssignments,
    IReadOnlyList<RecoveryActionDefinition> AvailableActions, IReadOnlyList<RecoverySubmission> CurrentRoundSubmissions,
    JsonElement? VisibleState, long Version, IReadOnlyList<ParticipantView> Participants);
public sealed record ParticipantView(Guid UserId, Guid? TeamId, bool IsReady, IReadOnlyList<string> Roles);
public sealed record HistoryItem(long Sequence, int RoundNumber, string Type, DateTimeOffset OccurredAt, JsonElement Data);
public sealed record RoundReadinessView(Guid UserId, int RoundNumber, string Phase, bool IsReady, DateTimeOffset ChangedAt);
public sealed record SubmissionInspection(Guid Id, int RoundNumber, Guid TeamId, Guid UserId, Guid RoleAssignmentId,
    string ActionCode, JsonElement Payload, DateTimeOffset SubmittedAt, string SubmittedPhase);
public sealed record SnapshotInspection(Guid TeamId, int RoundNumber, JsonElement State, DateTimeOffset CreatedAt);
public sealed record SessionInspection(Guid SessionId, string Status, string Phase, int RoundNumber, long Version,
    JsonElement Manifest, IReadOnlyList<ParticipantView> Participants, IReadOnlyList<RoundReadinessView> Readiness,
    IReadOnlyList<SubmissionInspection> Submissions, IReadOnlyList<SnapshotInspection> Snapshots,
    IReadOnlyList<HistoryItem> Events);

public interface IClassroomWorkflow
{
    ValueTask<Guid> CreateCourseAsync(Guid instructorId, string code, string name, CancellationToken ct);
    ValueTask<Guid> CreateClassroomAsync(Guid instructorId, Guid courseId, string name, CancellationToken ct);
    ValueTask EnrollAsync(Guid instructorId, Guid classroomId, Guid studentId, CancellationToken ct);
    ValueTask<Guid> CreateDefinitionAsync(Guid instructorId, string name, CancellationToken ct);
    ValueTask<Guid> PublishScenarioAsync(Guid instructorId, Guid definitionId, string name, ScenarioManifest manifest, CancellationToken ct);
    ValueTask<Guid> PublishScenarioDraftAsync(Guid instructorId, Guid draftId, string name, ScenarioManifest manifest, long expectedDraftVersion, CancellationToken ct);
    ValueTask<Guid> CreateSessionAsync(Guid instructorId, Guid classroomId, Guid scenarioVersionId, int seed, CancellationToken ct);
    ValueTask<Guid> CreateTeamAsync(Guid instructorId, Guid sessionId, string name, CancellationToken ct);
    ValueTask AddTeamMemberAsync(Guid instructorId, Guid sessionId, Guid teamId, Guid studentId, CancellationToken ct);
    ValueTask<Guid> AssignRoleAsync(Guid instructorId, Guid sessionId, Guid teamId, Guid studentId, string roleCode, CancellationToken ct);
    ValueTask RenameTeamAsync(Guid instructorId, Guid sessionId, Guid teamId, string name, long expectedVersion, string idempotencyKey, CancellationToken ct);
    ValueTask DeleteTeamAsync(Guid instructorId, Guid sessionId, Guid teamId, long expectedVersion, string idempotencyKey, CancellationToken ct);
    ValueTask RemoveTeamMemberAsync(Guid instructorId, Guid sessionId, Guid teamId, Guid studentId, long expectedVersion, string idempotencyKey, CancellationToken ct);
    ValueTask MoveTeamMemberAsync(Guid instructorId, Guid sessionId, Guid studentId, Guid targetTeamId, long expectedVersion, string idempotencyKey, CancellationToken ct);
    ValueTask UnassignRoleAsync(Guid instructorId, Guid sessionId, Guid assignmentId, long expectedVersion, string idempotencyKey, CancellationToken ct);
    ValueTask SetReadyAsync(Guid studentId, Guid sessionId, bool ready, CancellationToken ct);
    ValueTask SetRoundReadyAsync(Guid studentId, Guid sessionId, bool ready, CancellationToken ct);
    ValueTask StartSessionAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask PauseSessionAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask ResumeSessionAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask AdvancePhaseAsync(Guid instructorId, Guid sessionId, string targetPhase, CancellationToken ct);
    ValueTask<SessionRecoveryView> RecoverAsync(Guid userId, bool instructor, Guid sessionId, CancellationToken ct);
    ValueTask<IReadOnlyList<HistoryItem>> HistoryAsync(Guid userId, bool instructor, Guid sessionId, CancellationToken ct);
    ValueTask<SessionInspection> InspectAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
}
