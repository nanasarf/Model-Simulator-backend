using System.Text;
using System.Text.Json;
using SimulationPlatform.Application.Assessment;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Simulations.Economics.Macroeconomics;

public enum MacroAssessmentDimension { DirectionalPrediction, CausalMechanism, TradeoffAwareness, LagRecognition, ObjectiveAchievement, PolicyReasoning, EconomicOutcome }
public sealed record MacroAnalyticsSlice(Guid SubjectId, string SubjectType, string? RoleCode, int PredictionCount, int CorrectPredictions, decimal ConceptualScore, decimal PolicyReasoningScore, int ObjectivesAchieved, decimal ObjectiveScore, decimal EconomicOutcomeScore, int ShockResponses, int LagRecognitions, int PolicyConflicts);
public sealed record MacroSessionAnalytics(Guid SessionId, IReadOnlyList<MacroAnalyticsSlice> Students, IReadOnlyList<MacroAnalyticsSlice> Roles, IReadOnlyList<MacroAnalyticsSlice> Teams, MacroAnalyticsSlice Session, IReadOnlyList<AssessmentComment> Comments);
public sealed record MacroReplayQuarter(int Quarter, MacroState State, IReadOnlyList<MacroSubmissionView> Predictions, IReadOnlyList<MacroSubmissionView> Decisions, IReadOnlyList<CausalContribution> Contributions, IReadOnlyList<PolicyConflict> Tradeoffs, IReadOnlyList<PredictionAssessment> Assessments, IReadOnlyList<ObjectiveResult> Objectives, IReadOnlyList<string> StudentVisibleExplanations, IReadOnlyList<string> InstructorExplanations, IReadOnlyList<HistoryItem> Events);
public sealed record MacroSessionReport(Guid SessionId, MacroSessionAnalytics Analytics, IReadOnlyList<MacroReplayQuarter> Replay);

public interface IMacroLearningAnalytics
{
    ValueTask<MacroSessionAnalytics> AnalyzeAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask<MacroSessionReport> ReportAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    ValueTask<AssessmentComment> AddCommentAsync(Guid instructorId, Guid sessionId, string targetType, Guid targetId, string text, string key, CancellationToken ct);
    ValueTask<AssessmentComment> UpdateCommentAsync(Guid instructorId, Guid commentId, string text, long version, CancellationToken ct);
    ValueTask<IReadOnlyList<AssessmentComment>> CommentsAsync(Guid instructorId, Guid sessionId, CancellationToken ct);
    string ExportJson(MacroSessionReport report);
    string ExportCsv(MacroSessionReport report);
}

public sealed class MacroLearningAnalytics(IClassroomWorkflow workflow, IAssessmentCommentStore comments) : IMacroLearningAnalytics
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async ValueTask<MacroSessionAnalytics> AnalyzeAsync(Guid instructorId, Guid sessionId, CancellationToken ct)
    {
        var i = await MacroInspection(instructorId, sessionId, ct);
        var snapshots = i.Snapshots.Select(x => (x, State: x.State.Deserialize<MacroState>(JsonOptions))).Where(x => x.State is not null).ToArray();
        var students = i.Participants.Where(x => x.TeamId.HasValue).Select(p => Slice(p.UserId, "Student", p.Roles.FirstOrDefault(), i, snapshots)).ToArray();
        var roles = students.GroupBy(x => x.RoleCode ?? "UNASSIGNED").Select(g => Aggregate(Guid.Empty, "Role", g.Key, g)).ToArray();
        var teams = i.Participants.Where(x => x.TeamId.HasValue).GroupBy(x => x.TeamId!.Value).Select(g => Aggregate(g.Key, "Team", null, students.Where(s => g.Any(p => p.UserId == s.SubjectId)))).ToArray();
        return new(sessionId, students, roles, teams, Aggregate(sessionId, "Session", null, students), await comments.ListAsync(instructorId, sessionId, ct));
    }
    public async ValueTask<MacroSessionReport> ReportAsync(Guid instructorId, Guid sessionId, CancellationToken ct)
    {
        var i = await MacroInspection(instructorId, sessionId, ct); var analytics = await AnalyzeAsync(instructorId, sessionId, ct);
        var replay = i.Snapshots.Where(x => x.RoundNumber > 0).OrderBy(x => x.RoundNumber).Select(s => { var state = s.State.Deserialize<MacroState>(JsonOptions)!; var subs = i.Submissions.Where(x => x.TeamId == s.TeamId && x.RoundNumber == s.RoundNumber).ToArray(); var explanations = state.LastReport?.CausalExplanation ?? []; return new MacroReplayQuarter(s.RoundNumber, state, subs.Where(x => x.ActionCode == MacroActions.DirectionalPrediction).Select(View).ToArray(), subs.Where(x => x.ActionCode is not MacroActions.DirectionalPrediction and not MacroActions.TriggerShock).Select(View).ToArray(), state.LastReport?.Contributions ?? [], state.LastReport?.Conflicts ?? [], state.LastReport?.Assessments ?? [], state.LastReport?.Objectives ?? [], explanations.Select(x => x.Length > 240 ? x[..240] : x).ToArray(), explanations, i.Events.Where(x => x.RoundNumber == s.RoundNumber).ToArray()); }).ToArray();
        return new(sessionId, analytics, replay);
    }
    public ValueTask<AssessmentComment> AddCommentAsync(Guid instructorId, Guid sessionId, string targetType, Guid targetId, string text, string key, CancellationToken ct) => comments.AddAsync(instructorId, sessionId, targetType, targetId, text, key, ct);
    public ValueTask<AssessmentComment> UpdateCommentAsync(Guid instructorId, Guid commentId, string text, long version, CancellationToken ct) => comments.UpdateAsync(instructorId, commentId, text, version, ct);
    public ValueTask<IReadOnlyList<AssessmentComment>> CommentsAsync(Guid instructorId, Guid sessionId, CancellationToken ct) => comments.ListAsync(instructorId, sessionId, ct);
    public string ExportJson(MacroSessionReport report) => JsonSerializer.Serialize(report, JsonOptions);
    public string ExportCsv(MacroSessionReport report) { var b = new StringBuilder("subjectId,subjectType,role,predictions,correctPredictions,conceptualScore,policyReasoningScore,objectivesAchieved,objectiveScore,economicOutcomeScore,shockResponses,lagRecognitions,policyConflicts\n"); foreach (var x in report.Analytics.Students) b.AppendLine($"{x.SubjectId},{x.SubjectType},{x.RoleCode},{x.PredictionCount},{x.CorrectPredictions},{x.ConceptualScore},{x.PolicyReasoningScore},{x.ObjectivesAchieved},{x.ObjectiveScore},{x.EconomicOutcomeScore},{x.ShockResponses},{x.LagRecognitions},{x.PolicyConflicts}"); return b.ToString(); }
    private async ValueTask<SessionInspection> MacroInspection(Guid instructorId, Guid sessionId, CancellationToken ct) { var i = await workflow.InspectAsync(instructorId, sessionId, ct); if (i.Manifest.GetProperty("modelIdentifier").GetString() != "Economics.ShortRunMacro") throw new DomainException("macro.session_required", "This endpoint requires an Economics.ShortRunMacro session."); return i; }
    private static MacroAnalyticsSlice Slice(Guid id, string type, string? role, SessionInspection i, (SnapshotInspection x, MacroState? State)[] snapshots) { var own = i.Submissions.Where(x => x.UserId == id).ToArray(); var codes = own.Where(x => x.ActionCode != MacroActions.DirectionalPrediction).Select(x => x.ActionCode).ToHashSet(); var assessments = snapshots.SelectMany(x => x.State!.LastReport?.Assessments ?? []).Where(x => codes.Contains(x.ActionCode)).ToArray(); var count = assessments.Length; var correct = assessments.Sum(x => x.CorrectDirections); var score = count == 0 ? 0 : assessments.Average(x => x.ConceptualScore); var objectives = snapshots.SelectMany(x => x.State!.LastReport?.Objectives ?? []).ToArray(); var conflicts = snapshots.Sum(x => x.State!.LastReport?.Conflicts.Count ?? 0); return new(id, type, role, count, correct, score, count == 0 ? 0 : score, objectives.Count(x => x.Achieved), objectives.Length == 0 ? 0 : objectives.Count(x => x.Achieved) * 100m / objectives.Length, snapshots.LastOrDefault().State?.OutputGap is decimal gap ? Math.Max(0, 100 - Math.Abs(gap) * 5) : 0, i.Events.Count(x => x.Type.Contains("Shock", StringComparison.OrdinalIgnoreCase)), assessments.Count(x => x.Feedback.Any(f => f.Contains("lag", StringComparison.OrdinalIgnoreCase))), conflicts); }
    private static MacroAnalyticsSlice Aggregate(Guid id, string type, string? role, IEnumerable<MacroAnalyticsSlice> values) { var a = values.ToArray(); return new(id, type, role, a.Sum(x => x.PredictionCount), a.Sum(x => x.CorrectPredictions), a.Length == 0 ? 0 : a.Average(x => x.ConceptualScore), a.Length == 0 ? 0 : a.Average(x => x.PolicyReasoningScore), a.Sum(x => x.ObjectivesAchieved), a.Length == 0 ? 0 : a.Average(x => x.ObjectiveScore), a.Length == 0 ? 0 : a.Average(x => x.EconomicOutcomeScore), a.Sum(x => x.ShockResponses), a.Sum(x => x.LagRecognitions), a.Sum(x => x.PolicyConflicts)); }
    private static MacroSubmissionView View(SubmissionInspection x) => new(x.Id, x.UserId, x.ActionCode, x.Payload, x.SubmittedPhase, x.SubmittedAt);
}
