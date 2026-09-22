using System.Text.Json;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Simulations.Economics.Macroeconomics;

public sealed record MacroSubmissionView(Guid Id, Guid UserId, string ActionCode, JsonElement Payload,
    string SubmittedPhase, DateTimeOffset SubmittedAt);
public sealed record MacroPendingParticipant(Guid UserId, IReadOnlyList<string> Roles, bool HasSubmitted, bool IsReady);
public sealed record MacroTeamConsole(Guid TeamId, MacroState? FullState,
    IReadOnlyList<MacroSubmissionView> Decisions, IReadOnlyList<MacroSubmissionView> Predictions,
    IReadOnlyList<MacroPendingParticipant> PendingParticipants, IReadOnlyList<ScheduledMacroShock> ScheduledShocks,
    IReadOnlyList<CausalContribution> CausalContributions, IReadOnlyList<PolicyConflict> DetectedTradeoffs,
    IReadOnlyList<PredictionAssessment> ConceptualScores, IReadOnlyList<ObjectiveResult> EconomicObjectives);
public sealed record MacroInstructorConsole(Guid SessionId, string Status, string Phase, int Quarter, long Version,
    IReadOnlyList<MacroTeamConsole> Teams, IReadOnlyList<HistoryItem> EventHistory);

public sealed record MacroQuarterReplay(int Quarter, MacroState State, IReadOnlyList<MacroSubmissionView> Decisions,
    IReadOnlyList<MacroSubmissionView> Predictions, IReadOnlyList<HistoryItem> Events);
public sealed record MacroTeamDebrief(Guid TeamId, IReadOnlyList<MacroQuarterReplay> Quarters,
    decimal AverageConceptualScore, int EconomicObjectivesAchieved);
public sealed record MacroSessionDebrief(Guid SessionId, IReadOnlyList<MacroTeamDebrief> Teams,
    IReadOnlyList<HistoryItem> EventHistory);

public interface IMacroClassroomGameplay
{
    ValueTask<MacroInstructorConsole> GetInstructorConsoleAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask<MacroSessionDebrief> GetDebriefAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
}

public sealed class MacroClassroomGameplay(IClassroomWorkflow workflow) : IMacroClassroomGameplay
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        { PropertyNameCaseInsensitive = true };

    public async ValueTask<MacroInstructorConsole> GetInstructorConsoleAsync(Guid instructorId, Guid sessionId, CancellationToken ct)
    {
        var inspection = await InspectMacro(instructorId, sessionId, ct);
        var teams = inspection.Participants.Where(x => x.TeamId.HasValue).Select(x => x.TeamId!.Value).Distinct()
            .OrderBy(x => x).Select(teamId => BuildTeam(inspection, teamId)).ToArray();
        return new(inspection.SessionId, inspection.Status, inspection.Phase, inspection.RoundNumber,
            inspection.Version, teams, inspection.Events);
    }

    public async ValueTask<MacroSessionDebrief> GetDebriefAsync(Guid instructorId, Guid sessionId, CancellationToken ct)
    {
        var inspection = await InspectMacro(instructorId, sessionId, ct);
        var teams = inspection.Snapshots.Select(x => x.TeamId).Distinct().OrderBy(x => x).Select(teamId =>
        {
            var quarters = inspection.Snapshots.Where(x => x.TeamId == teamId && x.RoundNumber > 0)
                .OrderBy(x => x.RoundNumber).Select(snapshot =>
                {
                    var state = ParseState(snapshot.State);
                    var submissions = inspection.Submissions.Where(x => x.TeamId == teamId && x.RoundNumber == snapshot.RoundNumber).ToArray();
                    return new MacroQuarterReplay(snapshot.RoundNumber, state,
                        submissions.Where(IsDecision).Select(View).ToArray(),
                        submissions.Where(x => x.ActionCode == MacroActions.DirectionalPrediction).Select(View).ToArray(),
                        inspection.Events.Where(x => x.RoundNumber == snapshot.RoundNumber).ToArray());
                }).ToArray();
            var assessments = quarters.SelectMany(x => x.State.LastReport?.Assessments ?? []);
            var objectives = quarters.SelectMany(x => x.State.LastReport?.Objectives ?? []);
            return new MacroTeamDebrief(teamId, quarters, assessments.Any() ? assessments.Average(x => x.ConceptualScore) : 0,
                objectives.Count(x => x.Achieved));
        }).ToArray();
        return new(inspection.SessionId, teams, inspection.Events);
    }

    private async ValueTask<SessionInspection> InspectMacro(Guid instructorId, Guid sessionId, CancellationToken ct)
    {
        var inspection = await workflow.InspectAsync(instructorId, sessionId, ct);
        if (!inspection.Manifest.TryGetProperty("modelIdentifier", out var identifier) ||
            identifier.GetString() != "Economics.ShortRunMacro")
            throw new DomainException("macro.session_required", "This endpoint requires an Economics.ShortRunMacro session.");
        return inspection;
    }

    private static MacroTeamConsole BuildTeam(SessionInspection inspection, Guid teamId)
    {
        var latest = inspection.Snapshots.Where(x => x.TeamId == teamId).MaxBy(x => x.RoundNumber);
        var state = latest is null ? null : ParseState(latest.State);
        var current = inspection.Submissions.Where(x => x.TeamId == teamId && x.RoundNumber == inspection.RoundNumber).ToArray();
        var ready = inspection.Readiness.Where(x => x.RoundNumber == inspection.RoundNumber && x.Phase == inspection.Phase)
            .ToDictionary(x => x.UserId, x => x.IsReady);
        var pending = inspection.Participants.Where(x => x.TeamId == teamId).Select(person =>
            new MacroPendingParticipant(person.UserId, person.Roles,
                current.Any(x => x.UserId == person.UserId && x.SubmittedPhase == inspection.Phase),
                ready.GetValueOrDefault(person.UserId))).ToArray();
        return new(teamId, state, current.Where(IsDecision).Select(View).ToArray(),
            current.Where(x => x.ActionCode == MacroActions.DirectionalPrediction).Select(View).ToArray(), pending,
            state?.Configuration.ScheduledShocks?.Where(x => x.Round >= inspection.RoundNumber).ToArray() ?? [],
            state?.LastReport?.Contributions ?? [], state?.LastReport?.Conflicts ?? [],
            state?.LastReport?.Assessments ?? [], state?.LastReport?.Objectives ?? []);
    }

    private static bool IsDecision(SubmissionInspection x) => x.ActionCode is not (MacroActions.DirectionalPrediction or MacroActions.TriggerShock);
    private static MacroSubmissionView View(SubmissionInspection x) =>
        new(x.Id, x.UserId, x.ActionCode, x.Payload, x.SubmittedPhase, x.SubmittedAt);
    private static MacroState ParseState(JsonElement state) => state.Deserialize<MacroState>(JsonOptions)
        ?? throw new DomainException("macro.state_invalid", "The stored macroeconomic state is invalid.");
}
