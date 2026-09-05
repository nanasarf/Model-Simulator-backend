using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Infrastructure;
using SimulationPlatform.Infrastructure.Persistence;
using SimulationPlatform.Simulations.Economics.Macroeconomics;

namespace Tests.Integration;

public sealed class MacroScenarioAuthoringTests : IClassFixture<ClassroomDatabase>
{
    private readonly ClassroomDatabase database;
    public MacroScenarioAuthoringTests(ClassroomDatabase database) => this.database = database;

    [Fact]
    public async Task Draft_preview_publish_clone_archive_and_frozen_session_are_owned_and_versioned()
    {
        await using var scope = await database.CreateScope();
        var ids = await scope.BuildStartedMacroSession();
        var definition = await scope.Workflow.CreateDefinitionAsync(ids.Instructor, "Authored macro scenarios", default);
        var store = new EfScenarioDraftStore(scope.Platform, new SystemClock());
        var service = new MacroScenarioAuthoring(store, scope.Workflow, new ShortRunMacroModel());
        var content = service.Templates().Single(x => x.Code == "SUPPLY_DISRUPTION").Content;

        var created = await service.CreateAsync(ids.Instructor, definition, "Oil disruption", content, "draft-create", default);
        var retried = await service.CreateAsync(ids.Instructor, definition, "Oil disruption", content, "draft-create", default);
        Assert.Equal(created.Document.Id, retried.Document.Id);
        Assert.True(service.Validate(content).CanPublish);

        var preview1 = await service.PreviewAsync(ids.Instructor, created.Document.Id, 909, default);
        var preview2 = await service.PreviewAsync(ids.Instructor, created.Document.Id, 909, default);
        Assert.Equal(content.MaximumQuarters, preview1.Quarters.Count);
        Assert.True(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(preview1), JsonSerializer.SerializeToElement(preview2)));

        var scenarioVersion = await service.PublishAsync(ids.Instructor, created.Document.Id, created.Document.Version, default);
        var published = await service.GetAsync(ids.Instructor, created.Document.Id, default);
        Assert.Equal("Published", published.Document.Status);
        await Assert.ThrowsAsync<DomainException>(() => service.UpdateAsync(ids.Instructor, created.Document.Id,
            "Changed", content, published.Document.Version, default).AsTask());
        await Assert.ThrowsAsync<DomainException>(() => service.GetAsync(ids.OtherInstructor, created.Document.Id, default).AsTask());

        var clone = await service.CloneAsync(ids.Instructor, created.Document.Id, "Oil disruption variant", "clone-key", default);
        Assert.Equal("Draft", clone.Document.Status);
        await service.ArchiveAsync(ids.Instructor, clone.Document.Id, clone.Document.Version, default);
        Assert.Equal("Archived", (await service.GetAsync(ids.Instructor, clone.Document.Id, default)).Document.Status);

        var course = await scope.Workflow.CreateCourseAsync(ids.Instructor, "AUTH-" + Guid.NewGuid().ToString("N")[..6], "Authoring", default);
        var classroom = await scope.Workflow.CreateClassroomAsync(ids.Instructor, course, "Preview", default);
        var session = await scope.Workflow.CreateSessionAsync(ids.Instructor, classroom, scenarioVersion, 909, default);
        var frozen = await scope.Platform.SessionManifests.AsNoTracking().SingleAsync(x => x.SessionId == session);
        Assert.Contains("Oil disruption", (await scope.Platform.ScenarioVersions.AsNoTracking().SingleAsync(x => x.Id == scenarioVersion)).Name);
        Assert.Contains("Prepare the country", frozen.ManifestJson);
        Assert.True(await scope.Platform.AuditRecords.AnyAsync(x => x.ResourceType == "ScenarioDraft"));
        Assert.True(await scope.Platform.Outbox.AnyAsync(x => x.Type == "ScenarioDraftPublished"));
    }

    [Fact]
    public void Validation_reports_publication_blockers_and_warnings_without_equation_controls()
    {
        var valid = MacroTemplates.All[0].Content;
        var invalid = valid with
        {
            Briefing = "", MaximumQuarters = 2, LearningObjectives = [], DiscussionPrompts = [], DebriefPrompts = [],
            AllowedIntensities = [], ScheduledShocks = [new(3, "teacher_equation", PolicyIntensity.Strong)],
            TeamObjectives = valid.TeamObjectives with { InflationMinimum = 5, InflationMaximum = 2 }
        };
        var service = new MacroScenarioAuthoring(null!, null!, new ShortRunMacroModel());
        var report = service.Validate(invalid);
        Assert.False(report.CanPublish);
        Assert.Contains(report.Blockers, x => x.Code == "SHOCK_TIMING");
        Assert.Contains(report.Blockers, x => x.Code == "UNSUPPORTED_SHOCK");
        Assert.Contains(report.Blockers, x => x.Code == "OBJECTIVE_IMPOSSIBLE");
        Assert.Contains(report.Blockers, x => x.Code == "INTENSITY_REQUIRED");
        Assert.Contains(report.Warnings, x => x.Code == "DISCUSSION_PROMPTS_MISSING");
        Assert.DoesNotContain(typeof(MacroScenarioContent).GetProperties(), x => x.Name.Contains("Multiplier") || x.Name.Contains("Coefficient"));
    }

    [Fact]
    public async Task Draft_mutations_reject_stale_versions_and_idempotency_key_reuse()
    {
        await using var scope = await database.CreateScope();
        var ids = await scope.BuildStartedMacroSession();
        var definition = await scope.Workflow.CreateDefinitionAsync(ids.Instructor, "Concurrency authoring", default);
        var service = new MacroScenarioAuthoring(new EfScenarioDraftStore(scope.Platform, new SystemClock()),
            scope.Workflow, new ShortRunMacroModel());
        var content = service.Templates()[0].Content;
        var draft = await service.CreateAsync(ids.Instructor, definition, "Original", content, "same-key", default);

        await Assert.ThrowsAsync<DomainException>(() => service.CreateAsync(ids.Instructor, definition, "Different request",
            content, "same-key", default).AsTask());
        var changed = await service.UpdateAsync(ids.Instructor, draft.Document.Id, "Changed", content,
            draft.Document.Version, default);
        await Assert.ThrowsAsync<DomainException>(() => service.UpdateAsync(ids.Instructor, draft.Document.Id, "Stale",
            content, draft.Document.Version, default).AsTask());
        Assert.Equal(draft.Document.Version + 1, changed.Document.Version);
    }
}
