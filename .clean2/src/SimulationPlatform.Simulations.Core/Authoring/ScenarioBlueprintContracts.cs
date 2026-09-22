using System.Text.Json;

namespace SimulationPlatform.Simulations.Core.Authoring;

public sealed record ScenarioBlueprint(string SchemaVersion, string Title, string Summary, string StudentBriefing,
    string SimulationType, string ModelIdentifier, string ModelVersion, int RecommendedRounds,
    IReadOnlyList<LearningObjectiveBlueprint> LearningObjectives, IReadOnlyList<RoleBlueprint> Roles,
    IReadOnlyList<DecisionBlueprint> Decisions, IReadOnlyList<RuleBlueprint> Rules,
    JsonElement? StartingWorld, IReadOnlyList<ShockBlueprint> ShockPool,
    AssessmentRubricBlueprint? AssessmentRubric, string? InstructorNotes);
public sealed record LearningObjectiveBlueprint(string Code, string Description);
public sealed record RoleBlueprint(string Code, string DisplayName, string Description, IReadOnlyList<string> Responsibilities,
    IReadOnlyList<string> DecisionCodes, IReadOnlyList<string> VisibleIndicatorCodes, int MinimumParticipants, int MaximumParticipants);
public sealed record DecisionBlueprint(string Code, string DisplayName, string Description, IReadOnlyList<string> AllowedRoleCodes,
    string RequiredCapabilityCode, IReadOnlyList<string> AvailablePhases, IReadOnlyList<string> IntensityOptions);
public sealed record RuleBlueprint(string Id, string Name, string Description, string Trigger, JsonElement Conditions,
    JsonElement Effects, string Explanation, IReadOnlyList<string> LearningObjectiveCodes);
public sealed record ShockBlueprint(string Code, string Title, string StudentFacingDescription, string InstructorExplanation,
    string Type, string Severity, JsonElement Effects, int? SuggestedRound, IReadOnlyList<string> LearningObjectiveCodes);
public sealed record AssessmentRubricBlueprint(string Version, IReadOnlyList<AssessmentDimensionBlueprint> Dimensions);
public sealed record AssessmentDimensionBlueprint(string Code, string Description, decimal MaximumScore);
public sealed record BlueprintIssue(string Code, string Message, string Path, string Severity, string? SuggestedFix = null);
public sealed record ScenarioBlueprintValidationReport(bool IsValid, IReadOnlyList<BlueprintIssue> Blockers,
    IReadOnlyList<BlueprintIssue> Warnings, ScenarioBlueprint? NormalizedBlueprint = null);
