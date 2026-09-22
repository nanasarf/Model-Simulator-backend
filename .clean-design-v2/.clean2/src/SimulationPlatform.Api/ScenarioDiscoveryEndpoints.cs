using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Identity;
using SimulationPlatform.Identity.Authorization;
using SimulationPlatform.Infrastructure.Persistence;
using SimulationPlatform.Simulations.Core.Contracts;
using SimulationPlatform.Simulations.Economics.Macroeconomics;
using SimulationPlatform.Simulations.Economics.CompetitiveMarket;

namespace SimulationPlatform.Api;

public static class ScenarioDiscoveryEndpoints
{
    private const string Macro = "Economics.ShortRunMacro";
    private const string Market = "Economics.CompetitiveMarket";
    private const string ModelVersion = "1.0.0";
    private static readonly string[] Lifecycles = ["Draft", "Published", "Archived"];

    public static IEndpointRouteBuilder MapScenarioDiscovery(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");

        api.MapGet("/simulation-definitions/models", (IEnumerable<ISimulationModel> models) =>
            Results.Ok(models.Select(x => new AuthoringModelSummary(x.Descriptor.Identifier, x.Descriptor.Version,
                x.Descriptor.Name, x.Descriptor.Identifier is Macro or Market,
                x.Descriptor.Identifier is Macro or Market, "Available"))))
            .Produces<AuthoringModelSummary[]>();

        api.MapGet("/simulation-definitions", async (ClaimsPrincipal principal, PlatformDbContext db, CancellationToken ct) =>
        {
            var owner = UserId(principal);
            var definitions = await db.SimulationDefinitions.Where(x => x.OwnerUserId == owner)
                .OrderBy(x => x.Name).ThenBy(x => x.Id)
                .Select(x => new SimulationDefinitionSummary(x.Id, x.Name, new[] { Macro, Market })).ToListAsync(ct);
            if (definitions.Count == 0)
            {
                var row = new SimulationDefinitionRow { Id = Guid.NewGuid(), OwnerUserId = owner, Name = "My simulation scenarios" };
                db.SimulationDefinitions.Add(row);
                await db.SaveChangesAsync(ct);
                definitions = [new SimulationDefinitionSummary(row.Id, row.Name, new[] { Macro, Market })];
            }
            return Results.Ok(definitions);
        }).Produces<SimulationDefinitionSummary[]>().RequireAuthorization(PlatformPolicies.Instructor);

        api.MapGet("/simulation-definitions/{definitionId:guid}", async (Guid definitionId, ClaimsPrincipal principal,
            PlatformDbContext db, CancellationToken ct) =>
        {
            var owner = UserId(principal);
            var definition = await db.SimulationDefinitions.AsNoTracking()
                .Where(x => x.Id == definitionId && x.OwnerUserId == owner)
                .Select(x => new SimulationDefinitionSummary(x.Id, x.Name, new[] { Macro, Market })).SingleOrDefaultAsync(ct);
            if (definition is null) throw new DomainException("definition.not_found", "Simulation definition was not found.");
            return Results.Ok(definition);
        }).Produces<SimulationDefinitionSummary>().ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(PlatformPolicies.Instructor);

        api.MapGet("/scenarios", ListScenarios).Produces<ScenarioLibraryPage>().ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/scenarios/{scenarioId:guid}/versions", ScenarioVersions).Produces<PublishedScenarioVersionSummary[]>()
            .ProducesProblem(StatusCodes.Status404NotFound).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/scenario-versions/{versionId:guid}", ScenarioVersion).Produces<PublishedScenarioVersionSummary>()
            .ProducesProblem(StatusCodes.Status404NotFound).RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/simulation-definitions/{definitionId:guid}/scenario-versions", DefinitionVersions)
            .Produces<PublishedScenarioVersionSummary[]>().ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization(PlatformPolicies.Instructor);

        api.MapGet("/scenario-templates", (string? modelIdentifier, IMacroScenarioAuthoring macro,
            ICompetitiveMarketAuthoring market) =>
        {
            ValidateModel(modelIdentifier, allowNull: true);
            var result = new List<ScenarioTemplateSummary>();
            if (modelIdentifier is null or Macro)
                result.AddRange(macro.Templates().Select(x => new ScenarioTemplateSummary(x.Code, Macro, ModelVersion,
                    x.Name, x.Description, x.Content.LearningObjectives.FirstOrDefault(), true, true)));
            if (modelIdentifier is null or Market)
                result.AddRange(market.Templates().Select(x => new ScenarioTemplateSummary(x.Code, Market, ModelVersion,
                    x.Name, x.Content.Briefing, x.Content.LearningObjectives.FirstOrDefault(), true, true)));
            return Results.Ok(result);
        }).Produces<ScenarioTemplateSummary[]>().ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization(PlatformPolicies.Instructor);
        api.MapGet("/scenario-templates/{modelIdentifier}/{templateIdentifier}", (string modelIdentifier,
            string templateIdentifier, IMacroScenarioAuthoring macro, ICompetitiveMarketAuthoring market) =>
        {
            ValidateModel(modelIdentifier, allowNull: false);
            ScenarioTemplateSummary? template = modelIdentifier == Macro
                ? macro.Templates().Where(x => x.Code == templateIdentifier).Select(x => new ScenarioTemplateSummary(x.Code,
                    Macro, ModelVersion, x.Name, x.Description, x.Content.LearningObjectives.FirstOrDefault(), true, true)).SingleOrDefault()
                : market.Templates().Where(x => x.Code == templateIdentifier).Select(x => new ScenarioTemplateSummary(x.Code,
                    Market, ModelVersion, x.Name, x.Content.Briefing, x.Content.LearningObjectives.FirstOrDefault(), true, true)).SingleOrDefault();
            if (template is null) throw new DomainException("scenario_template.not_found", "Scenario template was not found.");
            return Results.Ok(template);
        }).Produces<ScenarioTemplateSummary>().ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequireAuthorization(PlatformPolicies.Instructor);
        return endpoints;
    }

    private static async Task<IResult> ListScenarios(string? status, string? modelIdentifier, string? search,
        bool? includeArchived, int? page, int? pageSize, ClaimsPrincipal principal, PlatformDbContext db, CancellationToken ct)
    {
        if (status is not null && !Lifecycles.Contains(status, StringComparer.Ordinal))
            throw new DomainException("scenario.lifecycle_filter_invalid", "Lifecycle status must be Draft, Published, or Archived.");
        ValidateModel(modelIdentifier, allowNull: true);
        var pageNumber = page is null or <= 0 ? 1 : page.Value;
        var requestedSize = pageSize is null or <= 0 ? 25 : pageSize.Value;
        if (requestedSize > 100) throw new DomainException("scenario.page_size_invalid", "Page size cannot exceed 100.");
        var owner = UserId(principal);
        var query = db.ScenarioDrafts.AsNoTracking().Where(x => x.OwnerUserId == owner);
        if (status is not null) query = query.Where(x => x.Status == status);
        else if (includeArchived != true) query = query.Where(x => x.Status != "Archived");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%"));
        }
        if (modelIdentifier == Macro) query = query.Where(x => EF.Functions.JsonContains(x.ContentJson, "{\"startingConditions\":{}}"));
        if (modelIdentifier == Market) query = query.Where(x => EF.Functions.JsonContains(x.ContentJson, "{\"configuration\":{}}"));
        var totalCount = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * requestedSize).Take(requestedSize).ToListAsync(ct);
        var publishedIds = rows.Where(x => x.PublishedScenarioVersionId.HasValue).Select(x => x.PublishedScenarioVersionId!.Value).ToArray();
        var versions = await db.ScenarioVersions.AsNoTracking().Where(x => publishedIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var items = rows.Select(x => Summarize(x, versions.GetValueOrDefault(x.PublishedScenarioVersionId ?? Guid.Empty))).ToArray();
        return Results.Ok(new ScenarioLibraryPage(items, pageNumber, requestedSize, totalCount));
    }

    private static async Task<IResult> ScenarioVersions(Guid scenarioId, ClaimsPrincipal principal, PlatformDbContext db, CancellationToken ct)
    {
        var owner = UserId(principal);
        var draft = await db.ScenarioDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scenarioId && x.OwnerUserId == owner, ct);
        if (draft is null) throw new DomainException("scenario.not_found", "Scenario was not found.");
        if (draft.PublishedScenarioVersionId is not Guid versionId) return Results.Ok(Array.Empty<PublishedScenarioVersionSummary>());
        var version = await OwnedVersion(versionId, owner, db, ct);
        return Results.Ok(new[] { VersionSummary(version, true, draft.Status == "Archived") });
    }

    private static async Task<IResult> ScenarioVersion(Guid versionId, ClaimsPrincipal principal, PlatformDbContext db, CancellationToken ct)
    {
        var owner = UserId(principal);
        var version = await OwnedVersion(versionId, owner, db, ct);
        var draft = await db.ScenarioDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.PublishedScenarioVersionId == versionId && x.OwnerUserId == owner, ct);
        return Results.Ok(VersionSummary(version, true, draft?.Status == "Archived"));
    }

    private static async Task<IResult> DefinitionVersions(Guid definitionId, ClaimsPrincipal principal, PlatformDbContext db, CancellationToken ct)
    {
        var owner = UserId(principal);
        if (!await db.SimulationDefinitions.AsNoTracking().AnyAsync(x => x.Id == definitionId && x.OwnerUserId == owner, ct))
            throw new DomainException("definition.not_found", "Simulation definition was not found.");
        var archived = await db.ScenarioDrafts.AsNoTracking().Where(x => x.OwnerUserId == owner && x.Status == "Archived" && x.PublishedScenarioVersionId != null)
            .Select(x => x.PublishedScenarioVersionId!.Value).ToListAsync(ct);
        var versions = await db.ScenarioVersions.AsNoTracking().Where(x => x.SimulationDefinitionId == definitionId)
            .OrderByDescending(x => x.Version).ThenBy(x => x.Id).ToListAsync(ct);
        var latest = versions.FirstOrDefault()?.Id;
        return Results.Ok(versions.Select(x => VersionSummary(x, x.Id == latest, archived.Contains(x.Id))));
    }

    private static async Task<ScenarioVersionRow> OwnedVersion(Guid id, Guid owner, PlatformDbContext db, CancellationToken ct) =>
        await db.ScenarioVersions.AsNoTracking().Where(x => x.Id == id)
            .Join(db.SimulationDefinitions.Where(x => x.OwnerUserId == owner), x => x.SimulationDefinitionId, x => x.Id, (version, _) => version)
            .SingleOrDefaultAsync(ct) ?? throw new DomainException("scenario_version.not_found", "Published scenario version was not found.");

    private static ScenarioSummary Summarize(ScenarioDraftRow row, ScenarioVersionRow? published)
    {
        using var json = JsonDocument.Parse(row.ContentJson);
        var root = json.RootElement;
        var isMacro = TryProperty(root, "startingConditions", out _);
        var model = published?.ModelIdentifier ?? (isMacro ? Macro : Market);
        var version = published?.ModelVersion ?? ModelVersion;
        var maximum = TryInt(root, isMacro ? "maximumQuarters" : "maximumRounds");
        var summary = TryString(root, "briefing");
        var archived = row.Status == "Archived";
        return new(row.Id, row.Id, row.SimulationDefinitionId, model, version, row.Name, summary, row.Status,
            row.PublishedScenarioVersionId, published?.Version, maximum, row.CreatedAt, row.UpdatedAt,
            published?.PublishedAt, row.Version, published is not null,
            published is null ? "A published version is required." : null);
    }

    private static PublishedScenarioVersionSummary VersionSummary(ScenarioVersionRow row, bool latest, bool archived) =>
        new(row.Id, row.SimulationDefinitionId, row.Name, row.ModelIdentifier, row.ModelVersion, row.Version,
            row.PublishedAt, latest, true, archived, true, null);
    private static int? TryInt(JsonElement root, string name) => TryProperty(root, name, out var value) && value.TryGetInt32(out var result) ? result : null;
    private static string? TryString(JsonElement root, string name) => TryProperty(root, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool TryProperty(JsonElement root, string name, out JsonElement value) => root.TryGetProperty(name, out value) || root.TryGetProperty(char.ToUpperInvariant(name[0]) + name[1..], out value);
    private static void ValidateModel(string? model, bool allowNull)
    {
        if (model is null && allowNull) return;
        if (model is not (Macro or Market)) throw new DomainException("simulation_model.unsupported", "The requested simulation model does not support scenario authoring.");
    }
    private static Guid UserId(ClaimsPrincipal principal) => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub"), out var id)
        ? id : throw new UnauthorizedAccessException("Authenticated subject is missing.");
}

public sealed record AuthoringModelSummary(string Identifier, string Version, string DisplayName,
    bool ScenarioAuthoringSupported, bool TemplatesAvailable, string Availability);
public sealed record SimulationDefinitionSummary(Guid Id, string DisplayName, IReadOnlyList<string> AuthorableModelIdentifiers);
public sealed record ScenarioTemplateSummary(string TemplateIdentifier, string ModelIdentifier, string ModelVersion,
    string Title, string? Description, string? LearningPurposeSummary, bool SystemProvided, bool CanCreateDraft);
public sealed record ScenarioSummary(Guid ScenarioId, Guid DraftId, Guid SimulationDefinitionId, string ModelIdentifier,
    string ModelVersion, string Title, string? Summary, string LifecycleStatus, Guid? PublishedVersionId,
    int? PublishedVersionNumber, int? MaximumRounds, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt, long ConcurrencyVersion, bool Launchable, string? LaunchabilityReason);
public sealed record ScenarioLibraryPage(IReadOnlyList<ScenarioSummary> Items, int Page, int PageSize, int TotalCount);
public sealed record PublishedScenarioVersionSummary(Guid PublishedVersionId, Guid SimulationDefinitionId, string Title,
    string ModelIdentifier, string ModelVersion, int ScenarioVersionNumber, DateTimeOffset PublishedAt,
    bool IsLatest, bool Immutable, bool SourceScenarioArchived, bool Launchable, string? LaunchabilityReason);
