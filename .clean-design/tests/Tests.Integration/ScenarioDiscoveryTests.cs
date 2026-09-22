using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Infrastructure.Persistence;

namespace Tests.Integration;

public sealed class ScenarioDiscoveryTests : IClassFixture<ClassroomDatabase>
{
    private readonly ClassroomDatabase database;
    public ScenarioDiscoveryTests(ClassroomDatabase database) => this.database = database;

    [Fact]
    public async Task Instructor_lists_only_owned_drafts_and_filters_model_and_lifecycle()
    {
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var definition = new SimulationDefinitionRow { Id = Guid.NewGuid(), OwnerUserId = owner, Name = "Economics lab" };
        var otherDefinition = new SimulationDefinitionRow { Id = Guid.NewGuid(), OwnerUserId = other, Name = "Private" };
        var macroVersion = Version(definition.Id, "Macro published", "Economics.ShortRunMacro", 1, now);
        var archivedVersion = Version(definition.Id, "Macro archived", "Economics.ShortRunMacro", 2, now.AddMinutes(1));
        await using (var scope = await database.CreateScope())
        {
            scope.Platform.AddRange(definition, otherDefinition, macroVersion, archivedVersion);
            scope.Platform.ScenarioDrafts.AddRange(
                Draft(owner, definition.Id, "Draft market", "Draft", MarketContent, now),
                Draft(owner, definition.Id, "Published macro", "Published", MacroContent, now.AddMinutes(1), macroVersion.Id),
                Draft(owner, definition.Id, "Archived macro", "Archived", MacroContent, now.AddMinutes(2), archivedVersion.Id),
                Draft(other, otherDefinition.Id, "Other instructor secret", "Draft", MacroContent, now.AddMinutes(3)));
            await scope.Platform.SaveChangesAsync();
        }
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        using var client = Client(factory, owner, "Instructor");
        var defaults = await client.GetFromJsonAsync<LibraryResponse>("/api/v1/scenarios");
        Assert.Equal(2, defaults!.TotalCount); Assert.DoesNotContain(defaults.Items, x => x.Title.Contains("secret"));
        var archived = await client.GetFromJsonAsync<LibraryResponse>("/api/v1/scenarios?status=Archived");
        Assert.Single(archived!.Items); Assert.Equal("Archived", archived.Items[0].LifecycleStatus); Assert.True(archived.Items[0].Launchable);
        var macro = await client.GetFromJsonAsync<LibraryResponse>("/api/v1/scenarios?modelIdentifier=Economics.ShortRunMacro&includeArchived=true");
        Assert.Equal(2, macro!.TotalCount); Assert.All(macro.Items, x => Assert.Equal("Economics.ShortRunMacro", x.ModelIdentifier));
        var published = await client.GetFromJsonAsync<LibraryResponse>("/api/v1/scenarios?status=Published");
        Assert.Single(published!.Items); Assert.True(published.Items[0].Launchable); Assert.Equal(macroVersion.Id, published.Items[0].PublishedVersionId);
    }

    [Fact]
    public async Task Published_versions_are_immutable_metadata_and_hidden_across_owners()
    {
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var definition = new SimulationDefinitionRow { Id = Guid.NewGuid(), OwnerUserId = owner, Name = "Owner definition" };
        var version = Version(definition.Id, "Published", "Economics.CompetitiveMarket", 1, DateTimeOffset.UtcNow);
        await using (var scope = await database.CreateScope()) { scope.Platform.AddRange(definition, version); await scope.Platform.SaveChangesAsync(); }
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        using var ownerClient = Client(factory, owner, "Instructor");
        var metadata = await ownerClient.GetFromJsonAsync<VersionResponse>($"/api/v1/scenario-versions/{version.Id}");
        Assert.True(metadata!.Immutable); Assert.True(metadata.Launchable); Assert.Equal("Economics.CompetitiveMarket", metadata.ModelIdentifier);
        using var otherClient = Client(factory, other, "Instructor");
        var hidden = await otherClient.GetAsync($"/api/v1/scenario-versions/{version.Id}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        var mutate = await ownerClient.PostAsJsonAsync($"/api/v1/scenario-versions/{version.Id}", new { name = "Changed" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, mutate.StatusCode);
    }

    [Fact]
    public async Task Models_and_templates_are_discoverable_without_internal_configuration()
    {
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        using var publicClient = factory.CreateClient();
        var modelsText = await publicClient.GetStringAsync("/api/v1/simulation-definitions/models");
        Assert.Contains("Economics.ShortRunMacro", modelsText); Assert.Contains("Economics.CompetitiveMarket", modelsText);
        Assert.DoesNotContain("coefficient", modelsText, StringComparison.OrdinalIgnoreCase);
        using var instructor = Client(factory, Guid.NewGuid(), "Instructor");
        var templates = await instructor.GetFromJsonAsync<TemplateResponse[]>("/api/v1/scenario-templates");
        Assert.Contains(templates!, x => x.ModelIdentifier == "Economics.ShortRunMacro");
        Assert.Contains(templates!, x => x.ModelIdentifier == "Economics.CompetitiveMarket");
        var raw = await instructor.GetStringAsync("/api/v1/scenario-templates");
        Assert.DoesNotContain("startingConditions", raw); Assert.DoesNotContain("configuration", raw);
        var immutable = await instructor.PutAsJsonAsync($"/api/v1/scenario-templates/{templates![0].ModelIdentifier}/{templates[0].TemplateIdentifier}", new { });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, immutable.StatusCode);
        var openApi = await publicClient.GetStringAsync("/swagger/v1/swagger.json");
        Assert.Contains("/api/v1/scenarios", openApi);
        Assert.Contains("/api/v1/scenario-versions/{versionId}", openApi);
        Assert.Contains("ScenarioLibraryPage", openApi);
    }

    [Fact]
    public async Task Students_cannot_enumerate_private_scenarios_or_templates()
    {
        using var factory = new DiscoveryApiFactory(database.ConnectionString);
        using var student = Client(factory, Guid.NewGuid(), "Student");
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/v1/scenarios")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/v1/simulation-definitions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/api/v1/scenario-templates")).StatusCode);
    }

    private static HttpClient Client(DiscoveryApiFactory factory, Guid user, string role)
    { var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Test-Role", role); client.DefaultRequestHeaders.Add("X-Test-User", user.ToString()); return client; }
    private static ScenarioDraftRow Draft(Guid owner, Guid definition, string name, string status, string content, DateTimeOffset at, Guid? published = null) => new() { Id = Guid.NewGuid(), OwnerUserId = owner, SimulationDefinitionId = definition, Name = name, Status = status, ContentJson = content, Version = 1, PublishedScenarioVersionId = published, CreatedAt = at, UpdatedAt = at };
    private static ScenarioVersionRow Version(Guid definition, string name, string model, int version, DateTimeOffset at) => new() { Id = Guid.NewGuid(), SimulationDefinitionId = definition, Version = version, Name = name, ModelIdentifier = model, ModelVersion = "1.0.0", ConfigurationJson = "{}", ManifestJson = "{}", ManifestHash = new byte[32], PublishedAt = at };
    private const string MacroContent = "{\"briefing\":\"Macro summary\",\"startingConditions\":{},\"maximumQuarters\":4}";
    private const string MarketContent = "{\"briefing\":\"Market summary\",\"configuration\":{},\"maximumRounds\":3}";
    private sealed record LibraryResponse(ScenarioItem[] Items, int Page, int PageSize, int TotalCount);
    private sealed record ScenarioItem(Guid ScenarioId, string Title, string LifecycleStatus, string ModelIdentifier, Guid? PublishedVersionId, bool Launchable);
    private sealed record VersionResponse(Guid PublishedVersionId, string ModelIdentifier, bool Immutable, bool Launchable);
    private sealed record TemplateResponse(string TemplateIdentifier, string ModelIdentifier);
}

public sealed class DiscoveryApiFactory(string connectionString) : SecureApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Platform", connectionString);
    }
}
