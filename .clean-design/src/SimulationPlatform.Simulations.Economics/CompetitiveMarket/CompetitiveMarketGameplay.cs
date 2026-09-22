using System.Text.Json;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Simulations.Economics.CompetitiveMarket;
public sealed record MarketTeamConsole(Guid TeamId, MarketState? State, IReadOnlyList<SubmissionInspection> Submissions, IReadOnlyList<HistoryItem> Events);
public sealed record MarketInstructorConsole(Guid SessionId, string Phase, int Round, IReadOnlyList<MarketTeamConsole> Teams, IReadOnlyList<HistoryItem> History);
public interface ICompetitiveMarketGameplay { ValueTask<MarketInstructorConsole> ConsoleAsync(Guid instructorId, Guid sessionId, CancellationToken ct); ValueTask<IReadOnlyList<MarketTeamConsole>> ReplayAsync(Guid instructorId, Guid sessionId, CancellationToken ct); }
public sealed class CompetitiveMarketGameplay(IClassroomWorkflow workflow) : ICompetitiveMarketGameplay
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public async ValueTask<MarketInstructorConsole> ConsoleAsync(Guid i, Guid s, CancellationToken ct) { var x = await Inspect(i, s, ct); var teams = x.Participants.Where(p => p.TeamId.HasValue).Select(p => p.TeamId!.Value).Distinct().Select(t => Build(x, t)).ToArray(); return new(s, x.Phase, x.RoundNumber, teams, x.Events); }
    public async ValueTask<IReadOnlyList<MarketTeamConsole>> ReplayAsync(Guid i, Guid s, CancellationToken ct) { var x = await Inspect(i, s, ct); return x.Participants.Where(p => p.TeamId.HasValue).Select(p => p.TeamId!.Value).Distinct().Select(t => Build(x, t)).ToArray(); }
    private async ValueTask<SessionInspection> Inspect(Guid i, Guid s, CancellationToken ct) { var x = await workflow.InspectAsync(i, s, ct); if (x.Manifest.GetProperty("modelIdentifier").GetString() != "Economics.CompetitiveMarket") throw new DomainException("market.session_required", "This endpoint requires a CompetitiveMarket session."); return x; }
    private static MarketTeamConsole Build(SessionInspection x, Guid team) { var snap = x.Snapshots.Where(s => s.TeamId == team).MaxBy(s => s.RoundNumber); return new(team, snap?.State.Deserialize<MarketState>(Options), x.Submissions.Where(s => s.TeamId == team).ToArray(), x.Events.Where(e => e.Data.TryGetProperty("teamId", out var t) && t.ValueKind == JsonValueKind.String && t.GetGuid() == team).ToArray()); }
}
