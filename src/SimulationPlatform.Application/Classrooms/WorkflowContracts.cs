using System.Text.Json;

namespace SimulationPlatform.Application.Classrooms;

public sealed record RoleManifest(string Code, string Name, int MinimumParticipants, int MaximumParticipants,
    HashSet<string> Capabilities);
public sealed record ActionManifest(string Code, string RequiredCapability, HashSet<string> AvailablePhases);
public sealed record RuleManifest(Guid Id, int Priority, string Effect, JsonElement Condition);
public sealed record ScenarioManifest(string ModelIdentifier, string ModelVersion, int ConfigurationVersion,
    JsonElement ModelConfiguration, List<string> Phases,
    Dictionary<string, HashSet<string>> AllowedTransitions,
    List<RoleManifest> Roles, List<ActionManifest> Actions, List<RuleManifest> Rules);

public sealed record SessionRecoveryView(Guid SessionId, string Status, string Phase, int RoundNumber,
    Guid? TeamId, IReadOnlyList<string> RoleCodes, JsonElement? VisibleState, long Version,
    IReadOnlyList<ParticipantView> Participants);
public sealed record ParticipantView(Guid UserId, Guid? TeamId, bool IsReady, IReadOnlyList<string> Roles);
public sealed record HistoryItem(long Sequence, int RoundNumber, string Type, DateTimeOffset OccurredAt, JsonElement Data);

public interface IClassroomWorkflow
{
    ValueTask<Guid> CreateCourseAsync(Guid instructorId, string code, string name, CancellationToken ct);
    ValueTask<Guid> CreateClassroomAsync(Guid instructorId, Guid courseId, string name, CancellationToken ct);
    ValueTask EnrollAsync(Guid instructorId, Guid classroomId, Guid studentId, CancellationToken ct);
    ValueTask<Guid> CreateDefinitionAsync(Guid instructorId, string name, CancellationToken ct);
    ValueTask<Guid> PublishScenarioAsync(Guid instructorId, Guid definitionId, string name, ScenarioManifest manifest, CancellationToken ct);
    ValueTask<Guid> CreateSessionAsync(Guid instructorId, Guid classroomId, Guid scenarioVersionId, int seed, CancellationToken ct);
    ValueTask<Guid> CreateTeamAsync(Guid instructorId, Guid sessionId, string name, CancellationToken ct);
    ValueTask AddTeamMemberAsync(Guid instructorId, Guid sessionId, Guid teamId, Guid studentId, CancellationToken ct);
    ValueTask<Guid> AssignRoleAsync(Guid instructorId, Guid sessionId, Guid teamId, Guid studentId, string roleCode, CancellationToken ct);
    ValueTask SetReadyAsync(Guid studentId, Guid sessionId, bool ready, CancellationToken ct);
    ValueTask StartSessionAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask PauseSessionAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask ResumeSessionAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask AdvancePhaseAsync(Guid instructorId, Guid sessionId, string targetPhase, CancellationToken ct);
    ValueTask<SessionRecoveryView> RecoverAsync(Guid userId, bool instructor, Guid sessionId, CancellationToken ct);
    ValueTask<IReadOnlyList<HistoryItem>> HistoryAsync(Guid userId, bool instructor, Guid sessionId, CancellationToken ct);
}
