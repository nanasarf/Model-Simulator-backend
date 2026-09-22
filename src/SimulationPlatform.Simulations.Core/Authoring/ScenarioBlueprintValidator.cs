using System.Text.Json;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Simulations.Core.Authoring;

public interface IAuthoringCapabilityCatalog
{
    AuthoringCatalog GetCatalog(string modelIdentifier, string modelVersion);
}
public sealed record AuthoringCatalog(string ModelIdentifier, string ModelVersion, IReadOnlySet<string> Roles,
    IReadOnlySet<string> Capabilities, IReadOnlySet<string> Actions, IReadOnlySet<string> Phases,
    IReadOnlySet<string> StateVariables, IReadOnlySet<string> ShockTypes,
    IReadOnlySet<string>? Indicators = null, IReadOnlySet<string>? RuleOperators = null,
    IReadOnlySet<string>? EffectDirections = null, IReadOnlySet<string>? IntensityLevels = null,
    IReadOnlySet<string>? AssessmentDimensions = null);

public sealed class ScenarioBlueprintValidator(IAuthoringCapabilityCatalog catalogs)
{
    public ScenarioBlueprintValidationReport Validate(ScenarioBlueprint blueprint)
    {
        var errors = new List<BlueprintIssue>();
        if (blueprint.SchemaVersion is not "1.0") errors.Add(new("scenario_blueprint.invalid_schema", "Unsupported schema version.", "schemaVersion", "Blocker"));
        AuthoringCatalog catalog;
        try { catalog = catalogs.GetCatalog(blueprint.ModelIdentifier, blueprint.ModelVersion); }
        catch { errors.Add(new("scenario_blueprint.unsupported_model", "The simulation model/version is not registered.", "modelIdentifier", "Blocker")); return new(false, errors, []); }
        if (string.IsNullOrWhiteSpace(blueprint.Title)) errors.Add(new("scenario_blueprint.title_required", "Title is required.", "title", "Blocker"));
        if (blueprint.RecommendedRounds <= 0) errors.Add(new("scenario_blueprint.rounds_invalid", "Recommended rounds must be positive.", "recommendedRounds", "Blocker"));
        foreach (var role in blueprint.Roles)
        {
            if (!catalog.Roles.Contains(role.Code)) errors.Add(new("scenario_blueprint.unsupported_role", $"Role '{role.Code}' is not supported.", "roles", "Blocker"));
            if (role.MaximumParticipants < Math.Max(1, role.MinimumParticipants)) errors.Add(new("scenario_blueprint.role_capacity_invalid", "Role capacity is invalid.", "roles", "Blocker"));
        }
        var roleCodes = blueprint.Roles.Select(x=>x.Code).ToHashSet();
        foreach (var decision in blueprint.Decisions)
        {
            if (!catalog.Actions.Contains(decision.Code)) errors.Add(new("scenario_blueprint.unsupported_action", $"Action '{decision.Code}' is not supported.", "decisions", "Blocker"));
            if (!catalog.Capabilities.Contains(decision.RequiredCapabilityCode)) errors.Add(new("scenario_blueprint.unsupported_capability", $"Capability '{decision.RequiredCapabilityCode}' is not supported.", "decisions", "Blocker"));
            if (decision.AllowedRoleCodes.Any(x=>!roleCodes.Contains(x))) errors.Add(new("scenario_blueprint.role_reference_invalid", "Decision references an unknown role.", "decisions", "Blocker"));
        }
        return new(errors.Count == 0, errors, []);
    }
}
