using System.Text.Json;
using SimulationPlatform.Application.Actions;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Simulations.Economics.Macroeconomics;

namespace Tests.Integration;

public sealed class MacroClassroomGameplayTests : IClassFixture<ClassroomDatabase>
{
    private readonly ClassroomDatabase database;
    public MacroClassroomGameplayTests(ClassroomDatabase database) => this.database = database;

    [Fact]
    public async Task Multi_team_round_enters_results_only_after_every_team_commits()
    {
        await using var scope = await database.CreateScope();
        var ids = await scope.BuildTwoTeamSession();
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Decision, default);
        for (var i = 0; i < 2; i++)
            await scope.Submit.HandleAsync(new(ids.Session, ids.Teams[i], ids.Students[i], ids.Assignments[i],
                "CHANGE_OUTPUT", JsonSerializer.SerializeToElement(new { direction = "increase" }), $"team-{i}"), default);
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Locked, default);
        await scope.Execute.HandleAsync(new(ids.Session, ids.Teams[0], ids.Instructor, Guid.NewGuid(), "team-a"), default);
        Assert.Equal(SessionPhases.Simulation, (await scope.Workflow.RecoverAsync(ids.Instructor, true, ids.Session, default)).Phase);
        await scope.Execute.HandleAsync(new(ids.Session, ids.Teams[1], ids.Instructor, Guid.NewGuid(), "team-b"), default);
        Assert.Equal(SessionPhases.Results, (await scope.Workflow.RecoverAsync(ids.Instructor, true, ids.Session, default)).Phase);
    }

    [Fact]
    public async Task Complete_quarter_enforces_prediction_decision_readiness_and_exposes_safe_role_results()
    {
        await using var scope = await database.CreateScope();
        var ids = await scope.BuildStartedMacroSession();
        var console = new MacroClassroomGameplay(scope.Workflow);
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, "Prediction", default);

        await Assert.ThrowsAsync<DomainException>(() =>
            scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Decision, default).AsTask());
        var targets = new[] { MacroActions.FiscalPolicy, MacroActions.MonetaryPolicy,
            MacroActions.BusinessStrategy, MacroActions.HouseholdLaborStance };
        for (var i = 0; i < ids.Students.Length; i++)
        {
            var prediction = JsonSerializer.SerializeToElement(new { targetActionCode = targets[i], output = "decrease",
                inflation = "increase", unemployment = "increase", explanation = "The action changes aggregate demand and costs." });
            await scope.Submit.HandleAsync(new(ids.Session, ids.Team, ids.Students[i], ids.Assignments[i],
                MacroActions.DirectionalPrediction, prediction, $"prediction-{i}"), default);
            if (i == 0)
                await Assert.ThrowsAsync<DomainException>(() => scope.Submit.HandleAsync(new(ids.Session, ids.Team,
                    ids.Students[i], ids.Assignments[i], MacroActions.DirectionalPrediction, prediction,
                    "prediction-duplicate"), default).AsTask());
            await scope.Workflow.SetRoundReadyAsync(ids.Students[i], ids.Session, true, default);
        }
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Decision, default);

        var decisions = new[]
        {
            Payload("expand", "Moderate"), Payload("tighten", "Moderate"),
            Payload("contract", "Mild"), Payload("support", "Mild")
        };
        for (var i = 0; i < ids.Students.Length; i++)
        {
            await scope.Submit.HandleAsync(new(ids.Session, ids.Team, ids.Students[i], ids.Assignments[i],
                targets[i], decisions[i], $"decision-{i}"), default);
            await scope.Workflow.SetRoundReadyAsync(ids.Students[i], ids.Session, true, default);
        }
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Locked, default);
        await scope.Execute.HandleAsync(new(ids.Session, ids.Team, ids.Instructor, Guid.NewGuid(), "macro-quarter"), default);

        var government = await scope.Workflow.RecoverAsync(ids.Students[0], false, ids.Session, default);
        Assert.Equal(SessionPhases.Results, government.Phase);
        Assert.True(government.VisibleState!.Value.TryGetProperty("fiscalBalance", out _));
        Assert.True(government.VisibleState.Value.TryGetProperty("causalExplanations", out _));
        Assert.False(government.VisibleState.Value.TryGetProperty("policyRate", out _));
        Assert.False(government.VisibleState.Value.TryGetProperty("configuration", out _));
        Assert.False(government.VisibleState.Value.TryGetProperty("lastReport", out _));
        Assert.False(government.VisibleState.Value.TryGetProperty("causalContributions", out _));

        var instructor = await console.GetInstructorConsoleAsync(ids.Instructor, ids.Session, default);
        var team = Assert.Single(instructor.Teams);
        Assert.Equal(4, team.Decisions.Count);
        Assert.Equal(4, team.Predictions.Count);
        Assert.Equal(5, team.CausalContributions.Count);
        Assert.NotEmpty(team.ConceptualScores);
        Assert.Contains(team.DetectedTradeoffs, x => x.Code is "STAGFLATION_TRADEOFF" or "STAGFLATION_CONDITIONS");
        Assert.Single(team.ScheduledShocks);
        Assert.NotNull(team.FullState!.Configuration);

        await Assert.ThrowsAsync<DomainException>(() => console.GetInstructorConsoleAsync(ids.OtherInstructor, ids.Session, default).AsTask());
    }

    [Fact]
    public async Task Discussion_rolls_to_next_quarter_and_debrief_replays_immutable_results()
    {
        await using var scope = await database.CreateScope();
        var ids = await scope.BuildStartedMacroSession();
        await CompleteMinimalQuarter(scope, ids);
        var gameplay = new MacroClassroomGameplay(scope.Workflow);
        var before = await gameplay.GetDebriefAsync(ids.Instructor, ids.Session, default);
        var firstQuarter = Assert.Single(Assert.Single(before.Teams).Quarters);
        var firstState = firstQuarter.State;

        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, "Discussion", default);
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Briefing, default);
        var recovery = await scope.Workflow.RecoverAsync(ids.Students[0], false, ids.Session, default);
        Assert.Equal(2, recovery.RoundNumber);
        Assert.Equal(SessionPhases.Briefing, recovery.Phase);

        var after = await gameplay.GetDebriefAsync(ids.Instructor, ids.Session, default);
        var replayed = Assert.Single(Assert.Single(after.Teams).Quarters);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(firstState), JsonSerializer.SerializeToElement(replayed.State)));
        Assert.Contains(after.EventHistory, x => x.RoundNumber == 2 && x.Type == "RoundPhaseChanged");
    }

    private static async Task CompleteMinimalQuarter(ClassroomScope scope, MacroWorkflowIds ids)
    {
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, "Prediction", default);
        var targets = new[] { MacroActions.FiscalPolicy, MacroActions.MonetaryPolicy, MacroActions.BusinessStrategy, MacroActions.HouseholdLaborStance };
        var directions = new[] { "neutral", "neutral", "neutral", "neutral" };
        for (var i = 0; i < 4; i++)
        {
            var prediction = JsonSerializer.SerializeToElement(new { targetActionCode = targets[i], output = "stable",
                inflation = "increase", unemployment = "increase", explanation = "Aggregate demand and supply determine the result." });
            await scope.Submit.HandleAsync(new(ids.Session, ids.Team, ids.Students[i], ids.Assignments[i], MacroActions.DirectionalPrediction,
                prediction, $"q1-p-{i}"), default);
            await scope.Workflow.SetRoundReadyAsync(ids.Students[i], ids.Session, true, default);
        }
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Decision, default);
        for (var i = 0; i < 4; i++)
        {
            await scope.Submit.HandleAsync(new(ids.Session, ids.Team, ids.Students[i], ids.Assignments[i], targets[i],
                Payload(directions[i], "Mild"), $"q1-d-{i}"), default);
            await scope.Workflow.SetRoundReadyAsync(ids.Students[i], ids.Session, true, default);
        }
        await scope.Workflow.AdvancePhaseAsync(ids.Instructor, ids.Session, SessionPhases.Locked, default);
        await scope.Execute.HandleAsync(new(ids.Session, ids.Team, ids.Instructor, Guid.NewGuid(), "q1"), default);
    }

    private static JsonElement Payload(string direction, string intensity) =>
        JsonSerializer.SerializeToElement(new { direction, intensity });
}
