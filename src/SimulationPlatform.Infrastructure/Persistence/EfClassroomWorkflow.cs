using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Definitions;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Identity;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class EfClassroomWorkflow(PlatformDbContext db, IdentityDataContext identity,
    ISimulationModelRegistry models, IClock clock) : IClassroomWorkflow
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<Guid> CreateCourseAsync(Guid actor, string code, string name, CancellationToken ct) => Tx(async () =>
    {
        Required(code, nameof(code)); Required(name, nameof(name));
        var organization = await db.Organizations.SingleOrDefaultAsync(x => x.OwnerUserId == actor, ct);
        if (organization is null)
        {
            organization = new OrganizationRow { Id = Guid.NewGuid(), OwnerUserId = actor, Name = "Personal workspace" };
            db.Organizations.Add(organization);
        }
        var row = new CourseRow { Id = Guid.NewGuid(), OrganizationId = organization.Id, OwnerUserId = actor,
            Code = code.Trim(), Name = name.Trim() };
        db.Courses.Add(row); Audit(actor, "Course.Created", "Course", row.Id); await db.SaveChangesAsync(ct); return row.Id;
    }, ct);

    public ValueTask<Guid> CreateClassroomAsync(Guid actor, Guid courseId, string name, CancellationToken ct) => Tx(async () =>
    {
        await OwnedCourse(actor, courseId, ct); Required(name, nameof(name));
        var row = new ClassroomRow { Id = Guid.NewGuid(), CourseId = courseId, Name = name.Trim() };
        db.Classrooms.Add(row); Audit(actor, "Classroom.Created", "Classroom", row.Id); await db.SaveChangesAsync(ct); return row.Id;
    }, ct);

    public ValueTask EnrollAsync(Guid actor, Guid classroomId, Guid studentId, CancellationToken ct) => Tx(async () =>
    {
        await OwnedClassroom(actor, classroomId, ct);
        if (!await identity.Users.AnyAsync(x => x.Id == studentId && x.IsActive, ct)) throw Error("student.not_found", "Student was not found.");
        if (!await identity.UserRoles.Join(identity.Roles, ur => ur.RoleId, role => role.Id, (ur, role) => new { ur.UserId, role.Name })
            .AnyAsync(x => x.UserId == studentId && x.Name == PlatformRoles.Student, ct)) throw Error("enrollment.not_student", "Only platform students may enroll.");
        if (!await db.Enrollments.AnyAsync(x => x.ClassroomId == classroomId && x.UserId == studentId, ct))
            db.Enrollments.Add(new EnrollmentRow { Id = Guid.NewGuid(), ClassroomId = classroomId, UserId = studentId, EnrolledAt = clock.UtcNow });
        Audit(actor, "Enrollment.Created", "Classroom", classroomId); await db.SaveChangesAsync(ct); return true;
    }, ct).AsVoid();

    public ValueTask<Guid> CreateDefinitionAsync(Guid actor, string name, CancellationToken ct) => Tx(async () =>
    {
        Required(name, nameof(name)); var row = new SimulationDefinitionRow { Id = Guid.NewGuid(), OwnerUserId = actor, Name = name.Trim() };
        db.SimulationDefinitions.Add(row); Audit(actor, "SimulationDefinition.Created", "SimulationDefinition", row.Id);
        await db.SaveChangesAsync(ct); return row.Id;
    }, ct);

    public ValueTask<Guid> PublishScenarioAsync(Guid actor, Guid definitionId, string name, ScenarioManifest manifest, CancellationToken ct) => Tx(async () =>
    {
        var definition = await db.SimulationDefinitions.SingleOrDefaultAsync(x => x.Id == definitionId && x.OwnerUserId == actor, ct)
            ?? throw Error("definition.not_found", "Simulation definition was not found.");
        ValidateManifest(manifest); models.Resolve(manifest.ModelIdentifier, manifest.ModelVersion);
        var version = (await db.ScenarioVersions.Where(x => x.SimulationDefinitionId == definition.Id).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions); var hash = SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson));
        var actionMap = manifest.Actions.ToDictionary(x => x.Code, x => new ActionDefinition(Guid.NewGuid(), x.Code,
            x.RequiredCapability, x.AvailablePhases), StringComparer.Ordinal);
        var transitions = manifest.AllowedTransitions.ToDictionary(x => x.Key,
            x => (IReadOnlySet<string>)x.Value, StringComparer.Ordinal);
        var domainScenario = new ScenarioVersion(Guid.NewGuid(), name.Trim(), version, manifest.ModelIdentifier,
            manifest.ModelVersion, manifest.Phases, transitions, actionMap);
        var row = new ScenarioVersionRow { Id = domainScenario.Id, SimulationDefinitionId = definitionId, Version = version,
            Name = name.Trim(), ModelIdentifier = manifest.ModelIdentifier, ModelVersion = manifest.ModelVersion,
            ConfigurationJson = JsonSerializer.Serialize(domainScenario, JsonOptions), ManifestJson = manifestJson,
            ManifestHash = hash, PublishedAt = clock.UtcNow };
        db.ScenarioVersions.Add(row);
        foreach (var rule in manifest.Rules)
        {
            _ = RuleAstParser.Parse(rule.Condition.GetRawText());
            db.Rules.Add(new RuleDefinitionRow { Id = rule.Id, ScenarioVersionId = row.Id, Priority = rule.Priority,
                Effect = rule.Effect, ConditionJson = rule.Condition.GetRawText(), Enabled = true });
        }
        db.Outbox.Add(Message("ScenarioPublished", row.Id, new { scenarioVersionId = row.Id, version }));
        Audit(actor, "Scenario.Published", "ScenarioVersion", row.Id); await db.SaveChangesAsync(ct); return row.Id;
    }, ct);

    public ValueTask<Guid> PublishScenarioDraftAsync(Guid actor, Guid draftId, string name, ScenarioManifest manifest, long expectedDraftVersion, CancellationToken ct) => Tx(async () =>
    {
        var draft = await db.ScenarioDrafts.SingleOrDefaultAsync(x => x.Id == draftId && x.OwnerUserId == actor, ct)
            ?? throw Error("scenario_draft.not_found", "Scenario draft was not found.");
        if (draft.Status != "Draft") throw Error("scenario_draft.immutable", "Only a draft can be published.");
        if (draft.Version != expectedDraftVersion) throw Error("concurrency.conflict", "The draft changed before publication.");
        ValidateManifest(manifest); models.Resolve(manifest.ModelIdentifier, manifest.ModelVersion);
        var version = (await db.ScenarioVersions.Where(x => x.SimulationDefinitionId == draft.SimulationDefinitionId)
            .MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
        var actionMap = manifest.Actions.ToDictionary(x => x.Code, x => new ActionDefinition(Guid.NewGuid(), x.Code,
            x.RequiredCapability, x.AvailablePhases), StringComparer.Ordinal);
        var transitions = manifest.AllowedTransitions.ToDictionary(x => x.Key, x => (IReadOnlySet<string>)x.Value, StringComparer.Ordinal);
        var domain = new ScenarioVersion(Guid.NewGuid(), name.Trim(), version, manifest.ModelIdentifier,
            manifest.ModelVersion, manifest.Phases, transitions, actionMap);
        db.ScenarioVersions.Add(new ScenarioVersionRow { Id = domain.Id, SimulationDefinitionId = draft.SimulationDefinitionId,
            Version = version, Name = name.Trim(), ModelIdentifier = manifest.ModelIdentifier, ModelVersion = manifest.ModelVersion,
            ConfigurationJson = JsonSerializer.Serialize(domain, JsonOptions), ManifestJson = manifestJson,
            ManifestHash = SHA256.HashData(Encoding.UTF8.GetBytes(manifestJson)), PublishedAt = clock.UtcNow });
        foreach (var rule in manifest.Rules)
        {
            _ = RuleAstParser.Parse(rule.Condition.GetRawText());
            db.Rules.Add(new RuleDefinitionRow { Id = rule.Id, ScenarioVersionId = domain.Id, Priority = rule.Priority,
                Effect = rule.Effect, ConditionJson = rule.Condition.GetRawText(), Enabled = true });
        }
        draft.Status = "Published"; draft.PublishedScenarioVersionId = domain.Id; draft.Version++; draft.UpdatedAt = clock.UtcNow;
        Message("ScenarioPublished", domain.Id, new { scenarioVersionId = domain.Id, version, draftId });
        Message("ScenarioDraftPublished", draftId, new { scenarioVersionId = domain.Id, version });
        Audit(actor, "ScenarioDraft.Published", "ScenarioDraft", draftId);
        await db.SaveChangesAsync(ct); return domain.Id;
    }, ct);

    public ValueTask<Guid> CreateSessionAsync(Guid actor, Guid classroomId, Guid scenarioId, int seed, CancellationToken ct) => Tx(async () =>
    {
        await OwnedClassroom(actor, classroomId, ct);
        var scenario = await db.ScenarioVersions.AsNoTracking()
            .Join(db.SimulationDefinitions.Where(x => x.OwnerUserId == actor), x => x.SimulationDefinitionId, x => x.Id, (scenario, _) => scenario)
            .SingleOrDefaultAsync(x => x.Id == scenarioId, ct)
            ?? throw Error("scenario.not_found", "Published scenario was not found.");
        var manifest = await PublishedManifest(scenario, ct);
        var session = new SessionRow { Id = Guid.NewGuid(), ClassroomId = classroomId, ScenarioVersionId = scenario.Id,
            ModelIdentifier = scenario.ModelIdentifier, ModelVersion = scenario.ModelVersion, Status = "Draft",
            Phase = manifest.Phases[0], RoundNumber = 1, Seed = seed, Version = 0, CreatedAt = clock.UtcNow };
        db.Sessions.Add(session);
        db.SessionManifests.Add(new SessionManifestRow { SessionId = session.Id,
            ManifestJson = JsonSerializer.Serialize(manifest, JsonOptions), ManifestHash = scenario.ManifestHash.ToArray(), FrozenAt = clock.UtcNow });
        Event(session.Id, 1, actor, "SessionCreated", new { classroomId, scenarioVersionId = scenario.Id, scenario.Version });
        Message("SessionStateChanged", session.Id, new { sessionId = session.Id, status = session.Status, phase = session.Phase });
        Audit(actor, "Session.Created", "Session", session.Id); await db.SaveChangesAsync(ct); return session.Id;
    }, ct);

    public ValueTask<Guid> CreateTeamAsync(Guid actor, Guid sessionId, string name, CancellationToken ct) => Tx(async () =>
    {
        var session = await OwnedSession(actor, sessionId, ct); EnsureDraft(session); Required(name, nameof(name));
        var row = new TeamRow { Id = Guid.NewGuid(), SessionId = sessionId, Name = name.Trim(), CreatedAt = clock.UtcNow };
        db.Teams.Add(row); Event(sessionId, session.RoundNumber, actor, "TeamCreated", new { teamId = row.Id, row.Name });
        Message("TeamChanged", sessionId, new { sessionId, teamId = row.Id }); await db.SaveChangesAsync(ct); return row.Id;
    }, ct);

    public ValueTask AddTeamMemberAsync(Guid actor, Guid sessionId, Guid teamId, Guid studentId, CancellationToken ct) => Tx(async () =>
    {
        var session = await OwnedSession(actor, sessionId, ct); EnsureDraft(session);
        if (!await db.Teams.AnyAsync(x => x.Id == teamId && x.SessionId == sessionId, ct)) throw Error("team.not_found", "Team was not found.");
        if (!await db.Enrollments.AnyAsync(x => x.ClassroomId == session.ClassroomId && x.UserId == studentId, ct)) throw Error("participant.not_enrolled", "Student is not enrolled in this classroom.");
        var participant = await db.Participants.SingleOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == studentId, ct);
        if (participant is null) db.Participants.Add(new ParticipantRow { Id = Guid.NewGuid(), SessionId = sessionId,
            UserId = studentId, TeamId = teamId, JoinedAt = clock.UtcNow });
        else { participant.TeamId = teamId; participant.IsReady = false; participant.ReadyAt = null; }
        Event(sessionId, session.RoundNumber, actor, "ParticipantJoined", new { teamId, userId = studentId });
        Message("ParticipantJoined", sessionId, new { sessionId, teamId, userId = studentId }); await db.SaveChangesAsync(ct); return true;
    }, ct).AsVoid();

    public ValueTask<Guid> AssignRoleAsync(Guid actor, Guid sessionId, Guid teamId, Guid studentId, string roleCode, CancellationToken ct) => Tx(async () =>
    {
        var session = await OwnedSession(actor, sessionId, ct); EnsureDraft(session); var manifest = await Manifest(sessionId, ct);
        var role = manifest.Roles.SingleOrDefault(x => x.Code == roleCode) ?? throw Error("role.not_found", "Role is not in the frozen manifest.");
        if (!await db.Participants.AnyAsync(x => x.SessionId == sessionId && x.TeamId == teamId && x.UserId == studentId, ct)) throw Error("participant.not_on_team", "Student is not on this team.");
        var count = await db.RoleAssignments.CountAsync(x => x.SessionId == sessionId && x.TeamId == teamId && x.RoleCode == roleCode && x.RevokedAt == null, ct);
        if (count >= role.MaximumParticipants) throw Error("role.capacity_reached", "Role participant capacity has been reached.");
        var row = new RoleAssignmentRow { Id = Guid.NewGuid(), SessionId = sessionId, TeamId = teamId,
            UserId = studentId, RoleDefinitionId = StableId(sessionId, roleCode), RoleCode = roleCode,
            CapabilitiesJson = JsonSerializer.Serialize(role.Capabilities, JsonOptions), AssignedAt = clock.UtcNow };
        db.RoleAssignments.Add(row); Event(sessionId, session.RoundNumber, actor, "RoleAssigned", new { teamId, userId = studentId, roleCode });
        Message("RoleAssignmentChanged", sessionId, new { sessionId, teamId, userId = studentId, roleCode });
        Audit(actor, "Role.Assigned", "Session", sessionId); await db.SaveChangesAsync(ct); return row.Id;
    }, ct);

    public ValueTask SetReadyAsync(Guid studentId, Guid sessionId, bool ready, CancellationToken ct) => Tx(async () =>
    {
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct) ?? throw Error("session.not_found", "Session was not found.");
        EnsureDraft(session); var participant = await db.Participants.SingleOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == studentId, ct)
            ?? throw Error("participant.not_found", "You are not a session participant.");
        if (participant.TeamId is null || !await db.RoleAssignments.AnyAsync(x => x.SessionId == sessionId && x.UserId == studentId && x.RevokedAt == null, ct))
            throw Error("participant.not_configured", "A team and role assignment are required before readiness.");
        participant.IsReady = ready; participant.ReadyAt = ready ? clock.UtcNow : null;
        Event(sessionId, session.RoundNumber, studentId, "ParticipantReadyChanged", new { userId = studentId, ready });
        Message("ParticipantReadyChanged", sessionId, new { sessionId, userId = studentId, ready }); await db.SaveChangesAsync(ct); return true;
    }, ct).AsVoid();

    public ValueTask SetRoundReadyAsync(Guid studentId, Guid sessionId, bool ready, CancellationToken ct) => Tx(async () =>
    {
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct)
            ?? throw Error("session.not_found", "Session was not found.");
        if (session.Status != "Running") throw Error("session.not_running", "Session must be running.");
        var manifest = await Manifest(sessionId, ct);
        if (!(manifest.ReadinessRequiredPhases ?? []).Contains(session.Phase))
            throw Error("readiness.phase_not_required", "The current phase does not accept readiness.");
        if (!await db.Participants.AnyAsync(x => x.SessionId == sessionId && x.UserId == studentId, ct))
            throw Error("participant.not_found", "You are not a session participant.");
        if (ready && !await db.ActionSubmissions.AnyAsync(x => x.SessionId == sessionId && x.RoundNumber == session.RoundNumber &&
            x.UserId == studentId && x.SubmittedPhase == session.Phase, ct))
            throw Error("readiness.submission_required", "Submit the required work for this phase before becoming ready.");
        var row = await db.RoundReadiness.SingleOrDefaultAsync(x => x.SessionId == sessionId && x.RoundNumber == session.RoundNumber &&
            x.Phase == session.Phase && x.UserId == studentId, ct);
        if (row is null) db.RoundReadiness.Add(new RoundReadinessRow { Id = Guid.NewGuid(), SessionId = sessionId,
            RoundNumber = session.RoundNumber, Phase = session.Phase, UserId = studentId, IsReady = ready, ChangedAt = clock.UtcNow });
        else { row.IsReady = ready; row.ChangedAt = clock.UtcNow; }
        Event(sessionId, session.RoundNumber, studentId, "RoundReadinessChanged", new { userId = studentId, session.Phase, ready });
        Message("ParticipantReadyChanged", sessionId, new { sessionId, roundNumber = session.RoundNumber, phase = session.Phase, userId = studentId, ready });
        await db.SaveChangesAsync(ct); return true;
    }, ct).AsVoid();

    public ValueTask StartSessionAsync(Guid actor, Guid sessionId, CancellationToken ct) => Tx(async () =>
    {
        var session = await OwnedSession(actor, sessionId, ct); EnsureDraft(session); var manifest = await Manifest(sessionId, ct);
        var teams = await db.Teams.Where(x => x.SessionId == sessionId).ToListAsync(ct);
        var participants = await db.Participants.Where(x => x.SessionId == sessionId).ToListAsync(ct);
        if (teams.Count == 0 || participants.Count == 0) throw Error("session.empty", "At least one configured team is required.");
        if (participants.Any(x => x.TeamId is null || !x.IsReady)) throw Error("session.not_ready", "Every participant must have a team and be ready.");
        var assignments = await db.RoleAssignments.Where(x => x.SessionId == sessionId && x.RevokedAt == null).ToListAsync(ct);
        foreach (var team in teams)
        {
            var members = participants.Where(x => x.TeamId == team.Id).ToArray();
            if (members.Length == 0 || members.Any(x => assignments.All(a => a.TeamId != team.Id || a.UserId != x.UserId))) throw Error("team.not_ready", "Every team member needs a role.");
            foreach (var role in manifest.Roles.Where(x => x.MinimumParticipants > 0))
                if (assignments.Count(x => x.TeamId == team.Id && x.RoleCode == role.Code) < role.MinimumParticipants) throw Error("role.minimum_not_met", $"Team {team.Name} does not meet role {role.Name}'s minimum.");
            var model = models.Resolve(session.ModelIdentifier, session.ModelVersion);
            var state = await model.InitializeAsync(new(manifest.ModelConfiguration, StableSeed(session.Seed, team.Id, 0)), ct);
            db.Snapshots.Add(new SnapshotRow { Id = Guid.NewGuid(), SessionId = sessionId, TeamId = team.Id, RoundNumber = 0,
                ModelIdentifier = session.ModelIdentifier, ModelVersion = session.ModelVersion, StateJson = state.GetRawText(), CreatedAt = clock.UtcNow });
        }
        session.Status = "Running"; session.StartedAt = clock.UtcNow; session.Version++;
        Event(sessionId, session.RoundNumber, actor, "SessionStarted", new { session.Phase });
        Message("SessionStateChanged", sessionId, new { sessionId, status = session.Status, phase = session.Phase });
        Audit(actor, "Session.Started", "Session", sessionId); await db.SaveChangesAsync(ct); return true;
    }, ct).AsVoid();

    public ValueTask PauseSessionAsync(Guid actor, Guid sessionId, CancellationToken ct) => ChangeStatus(actor, sessionId, "Running", "Paused", "SessionPaused", ct);
    public ValueTask ResumeSessionAsync(Guid actor, Guid sessionId, CancellationToken ct) => ChangeStatus(actor, sessionId, "Paused", "Running", "SessionResumed", ct);

    public ValueTask AdvancePhaseAsync(Guid actor, Guid sessionId, string target, CancellationToken ct) => Tx(async () =>
    {
        var session = await OwnedSession(actor, sessionId, ct); if (session.Status != "Running") throw Error("session.not_running", "Session must be running.");
        var manifest = await Manifest(sessionId, ct);
        if (!manifest.AllowedTransitions.TryGetValue(session.Phase, out var allowed) || !allowed.Contains(target)) throw Error("round.invalid_transition", "Phase transition is not allowed.");
        if (target == SessionPhases.Simulation) throw Error("round.execution_required", "Enter Simulation through the execute command.");
        if ((manifest.ReadinessRequiredPhases ?? []).Contains(session.Phase))
        {
            var participantCount = await db.Participants.CountAsync(x => x.SessionId == sessionId, ct);
            var readyCount = await db.RoundReadiness.CountAsync(x => x.SessionId == sessionId && x.RoundNumber == session.RoundNumber &&
                x.Phase == session.Phase && x.IsReady, ct);
            if (participantCount == 0 || readyCount != participantCount)
                throw Error("round.not_ready", "Every participant must be ready before advancing this phase.");
        }
        var previous = session.Phase;
        if (target == manifest.Phases[0] && previous != target)
        {
            if (manifest.MaximumRounds is int maximum && session.RoundNumber >= maximum)
                throw Error("round.maximum_reached", "The configured maximum number of rounds has been reached.");
            session.RoundNumber++;
        }
        session.Phase = target; session.Version++;
        if (target == SessionPhases.Completed) { session.Status = "Completed"; session.CompletedAt = clock.UtcNow; }
        Event(sessionId, session.RoundNumber, actor, "RoundPhaseChanged", new { previous, current = target });
        Message("RoundPhaseChanged", sessionId, new { sessionId, previous, current = target, session.Version });
        Audit(actor, "Round.PhaseAdvanced", "Session", sessionId); await db.SaveChangesAsync(ct); return true;
    }, ct).AsVoid();

    public async ValueTask<SessionRecoveryView> RecoverAsync(Guid userId, bool instructor, Guid sessionId, CancellationToken ct)
    {
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, ct) ?? throw Error("session.not_found", "Session was not found.");
        if (instructor) await OwnedSession(userId, sessionId, ct);
        var participant = await db.Participants.AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == userId, ct);
        if (!instructor && participant is null) throw Error("session.not_found", "Session was not found.");
        var manifest = await Manifest(sessionId, ct);
        var assignments = await db.RoleAssignments.AsNoTracking().Where(x => x.SessionId == sessionId && x.RevokedAt == null).ToListAsync(ct);
        var visibleAssignments = instructor ? assignments : assignments.Where(x => x.UserId == userId).ToList();
        var capabilities = visibleAssignments.SelectMany(x => JsonSerializer.Deserialize<HashSet<string>>(x.CapabilitiesJson, JsonOptions) ?? []).ToHashSet();
        var availableActions = manifest.Actions
            .Where(x => instructor || capabilities.Contains(x.RequiredCapability))
            .Select(x => new RecoveryActionDefinition(x.Code, x.RequiredCapability, x.AvailablePhases, x.Constraints)).ToArray();
        var currentRows = await db.ActionSubmissions.AsNoTracking()
            .Where(x => x.SessionId == sessionId && x.RoundNumber == session.RoundNumber)
            .OrderBy(x => x.SubmittedAt).ToListAsync(ct);
        var visibleSubmissions = instructor ? currentRows : currentRows.Where(x => x.UserId == userId).ToList();
        var currentSubmissions = visibleSubmissions.Select(x => new RecoverySubmission(x.Id, x.RoleAssignmentId, x.ActionCode,
            "Submitted", x.SubmittedAt, instructor || x.UserId == userId ? JsonDocument.Parse(x.PayloadJson).RootElement.Clone() : null)).ToArray();
        JsonElement? visible = null;
        if (participant?.TeamId is Guid teamId)
        {
            var snapshot = await db.Snapshots.AsNoTracking().Where(x => x.SessionId == sessionId && x.TeamId == teamId).OrderByDescending(x => x.RoundNumber).FirstOrDefaultAsync(ct);
            var model = models.Resolve(session.ModelIdentifier, session.ModelVersion);
            var authoritativeState = snapshot is not null
                ? JsonDocument.Parse(snapshot.StateJson).RootElement.Clone()
                : await model.InitializeAsync(new(manifest.ModelConfiguration, session.Seed), ct);
            visible = await model.GenerateVisibleStateAsync(new(authoritativeState, capabilities), ct);
        }
        var people = await db.Participants.AsNoTracking().Where(x => x.SessionId == sessionId).Select(x => new { x.UserId, x.TeamId, x.IsReady }).ToListAsync(ct);
        return new(session.Id, session.Status, session.Phase, session.RoundNumber, session.ModelIdentifier, session.ModelVersion,
            participant?.TeamId,
            visibleAssignments.Select(x => new RecoveryRoleAssignment(x.Id, x.TeamId, x.RoleCode,
                JsonSerializer.Deserialize<HashSet<string>>(x.CapabilitiesJson, JsonOptions) ?? [])).ToArray(),
            availableActions, currentSubmissions, visible, session.Version,
            people.Select(x => new ParticipantView(x.UserId, x.TeamId, x.IsReady, assignments.Where(a => a.UserId == x.UserId).Select(a => a.RoleCode).ToArray())).ToArray());
    }

    public async ValueTask<IReadOnlyList<HistoryItem>> HistoryAsync(Guid userId, bool instructor, Guid sessionId, CancellationToken ct)
    {
        Guid? teamId = null;
        if (instructor) await OwnedSession(userId, sessionId, ct);
        else teamId = (await db.Participants.AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == userId, ct))?.TeamId
            ?? throw Error("session.not_found", "Session was not found.");
        var rows = await db.Events.AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(ct);
        return rows.Where(x => instructor || VisibleToTeam(x.DataJson, teamId!.Value)).Select(x => new HistoryItem(x.Sequence, x.RoundNumber,
            x.Type, x.OccurredAt, JsonDocument.Parse(x.DataJson).RootElement.Clone())).ToArray();
    }

    public async ValueTask<SessionInspection> InspectAsync(Guid instructorId, Guid sessionId, CancellationToken ct)
    {
        var session = await OwnedSession(instructorId, sessionId, ct);
        var manifestJson = (await db.SessionManifests.AsNoTracking().SingleAsync(x => x.SessionId == sessionId, ct)).ManifestJson;
        var assignments = await db.RoleAssignments.AsNoTracking().Where(x => x.SessionId == sessionId && x.RevokedAt == null).ToListAsync(ct);
        var people = await db.Participants.AsNoTracking().Where(x => x.SessionId == sessionId).ToListAsync(ct);
        var readiness = await db.RoundReadiness.AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.RoundNumber).ThenBy(x => x.Phase).ToListAsync(ct);
        var submissions = await db.ActionSubmissions.AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.RoundNumber).ThenBy(x => x.SubmittedAt).ToListAsync(ct);
        var snapshots = await db.Snapshots.AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.TeamId).ThenBy(x => x.RoundNumber).ToListAsync(ct);
        var events = await db.Events.AsNoTracking().Where(x => x.SessionId == sessionId).OrderBy(x => x.Sequence).ToListAsync(ct);
        return new(session.Id, session.Status, session.Phase, session.RoundNumber, session.Version,
            JsonDocument.Parse(manifestJson).RootElement.Clone(),
            people.Select(x => new ParticipantView(x.UserId, x.TeamId, x.IsReady,
                assignments.Where(a => a.UserId == x.UserId).Select(a => a.RoleCode).ToArray())).ToArray(),
            readiness.Select(x => new RoundReadinessView(x.UserId, x.RoundNumber, x.Phase, x.IsReady, x.ChangedAt)).ToArray(),
            submissions.Select(x => new SubmissionInspection(x.Id, x.RoundNumber, x.TeamId, x.UserId, x.RoleAssignmentId,
                x.ActionCode, JsonDocument.Parse(x.PayloadJson).RootElement.Clone(), x.SubmittedAt, x.SubmittedPhase)).ToArray(),
            snapshots.Select(x => new SnapshotInspection(x.TeamId, x.RoundNumber,
                JsonDocument.Parse(x.StateJson).RootElement.Clone(), x.CreatedAt)).ToArray(),
            events.Select(x => new HistoryItem(x.Sequence, x.RoundNumber, x.Type, x.OccurredAt,
                JsonDocument.Parse(x.DataJson).RootElement.Clone())).ToArray());
    }

    private ValueTask ChangeStatus(Guid actor, Guid sessionId, string expected, string target, string eventType, CancellationToken ct) => Tx(async () =>
    {
        var session = await OwnedSession(actor, sessionId, ct); if (session.Status != expected) throw Error("session.status_conflict", $"Session must be {expected}.");
        session.Status = target; session.Version++; Event(sessionId, session.RoundNumber, actor, eventType, new { status = target });
        Message(eventType, sessionId, new { sessionId, status = target, session.Version }); Audit(actor, $"Session.{target}", "Session", sessionId);
        await db.SaveChangesAsync(ct); return true;
    }, ct).AsVoid();

    private async Task<CourseRow> OwnedCourse(Guid actor, Guid id, CancellationToken ct) => await db.Courses.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == actor, ct) ?? throw Error("course.not_found", "Course was not found.");
    private async Task<ClassroomRow> OwnedClassroom(Guid actor, Guid id, CancellationToken ct) => await db.Classrooms.Join(db.Courses, x => x.CourseId, x => x.Id, (room, course) => new { room, course.OwnerUserId }).Where(x => x.room.Id == id && x.OwnerUserId == actor).Select(x => x.room).SingleOrDefaultAsync(ct) ?? throw Error("classroom.not_found", "Classroom was not found.");
    private async Task<SessionRow> OwnedSession(Guid actor, Guid id, CancellationToken ct) => await db.Sessions.Join(db.Classrooms, x => x.ClassroomId, x => x.Id, (session, room) => new { session, room.CourseId }).Join(db.Courses, x => x.CourseId, x => x.Id, (x, course) => new { x.session, course.OwnerUserId }).Where(x => x.session.Id == id && x.OwnerUserId == actor).Select(x => x.session).SingleOrDefaultAsync(ct) ?? throw Error("session.not_found", "Session was not found.");
    private async Task<ScenarioManifest> Manifest(Guid sessionId, CancellationToken ct) => JsonSerializer.Deserialize<ScenarioManifest>((await db.SessionManifests.AsNoTracking().SingleAsync(x => x.SessionId == sessionId, ct)).ManifestJson, JsonOptions) ?? throw Error("manifest.invalid", "Frozen session manifest is invalid.");
    private Task<ScenarioManifest> PublishedManifest(ScenarioVersionRow scenario, CancellationToken ct) =>
        Task.FromResult(JsonSerializer.Deserialize<ScenarioManifest>(scenario.ManifestJson, JsonOptions)
            ?? throw Error("scenario.invalid", "Published scenario manifest is invalid."));

    private void ValidateManifest(ScenarioManifest manifest)
    {
        if (manifest.Phases.Count == 0 || manifest.Roles.Count == 0 || manifest.Actions.Count == 0) throw Error("manifest.incomplete", "Phases, roles, and actions are required.");
        if (manifest.Phases.Distinct().Count() != manifest.Phases.Count || manifest.Roles.Select(x => x.Code).Distinct().Count() != manifest.Roles.Count || manifest.Actions.Select(x => x.Code).Distinct().Count() != manifest.Actions.Count) throw Error("manifest.duplicate", "Manifest codes and phases must be unique.");
        var capabilities = manifest.Roles.SelectMany(x => x.Capabilities).ToHashSet();
        if (manifest.Roles.Any(x => x.MinimumParticipants < 0 || x.MaximumParticipants < Math.Max(1, x.MinimumParticipants))) throw Error("manifest.role_capacity", "Role capacity is invalid.");
        if (manifest.Actions.Any(x => !capabilities.Contains(x.RequiredCapability) || x.AvailablePhases.Any(p => !manifest.Phases.Contains(p)))) throw Error("manifest.action_invalid", "Action capability or phase is invalid.");
        if (manifest.AllowedTransitions.Any(x => !manifest.Phases.Contains(x.Key) || x.Value.Any(p => !manifest.Phases.Contains(p)))) throw Error("manifest.transition_invalid", "Transition references an unknown phase.");
        if ((manifest.ReadinessRequiredPhases ?? []).Any(x => !manifest.Phases.Contains(x)) || manifest.MaximumRounds is <= 0)
            throw Error("manifest.lifecycle_invalid", "Readiness phases and maximum rounds must be valid.");
        if (manifest.Rules.Any(x => x.Effect is not ("Allow" or "Deny"))) throw Error("manifest.rule_effect", "Rule effect must be Allow or Deny.");
    }

    private static bool VisibleToTeam(string json, Guid teamId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var hasTeam = root.TryGetProperty("teamId", out var value) || root.TryGetProperty("TeamId", out value);
        return !hasTeam || value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var parsed) && parsed == teamId;
    }
    private static Guid StableId(Guid sessionId, string code) => new(SHA256.HashData(Encoding.UTF8.GetBytes($"{sessionId:N}:{code}"))[..16]);
    private static int StableSeed(int seed, Guid teamId, int round) => BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{teamId:N}:{round}")), 0);
    private static void Required(string value, string name) { if (string.IsNullOrWhiteSpace(value)) throw Error("validation.required", $"{name} is required."); }
    private static void EnsureDraft(SessionRow session) { if (session.Status != "Draft") throw Error("session.frozen", "Running session configuration is immutable."); }
    private static DomainException Error(string code, string message) => new(code, message);
    private void Audit(Guid actor, string action, string type, Guid id) => db.AuditRecords.Add(new AuditRow { Id = Guid.NewGuid(), ActorUserId = actor, Action = action, ResourceType = type, ResourceId = id.ToString(), TraceId = "workflow", MetadataJson = "{}", OccurredAt = clock.UtcNow });
    private void Event(Guid sessionId, int round, Guid? actor, string type, object data) => db.Events.Add(new EventRow { Id = Guid.NewGuid(), SessionId = sessionId, RoundNumber = round, ActorId = actor, Type = type, DataJson = JsonSerializer.Serialize(data, JsonOptions), OccurredAt = clock.UtcNow });
    private OutboxMessage Message(string type, Guid aggregate, object payload) { var row = new OutboxMessage { Id = Guid.NewGuid(), Type = type, AggregateId = aggregate, PayloadJson = JsonSerializer.Serialize(payload, JsonOptions), OccurredAt = clock.UtcNow, NextAttemptAt = clock.UtcNow }; db.Outbox.Add(row); return row; }
    private async ValueTask<T> Tx<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () => { await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct); var result = await operation(); await transaction.CommitAsync(ct); return result; });
    }
}

internal static class ValueTaskExtensions
{
    public static async ValueTask AsVoid<T>(this ValueTask<T> task) => _ = await task;
}
