namespace SimulationPlatform.Domain.Definitions;

public sealed record CapabilityDefinition(Guid Id, string Code, string Name);

public sealed record RoleDefinition(
    Guid Id,
    string Code,
    string Name,
    IReadOnlySet<string> CapabilityCodes);

public sealed record ActionDefinition(
    Guid Id,
    string Code,
    string RequiredCapability,
    IReadOnlySet<string> AvailablePhases);

public sealed record ScenarioVersion(
    Guid Id,
    string Name,
    int Version,
    string ModelIdentifier,
    string ModelVersion,
    IReadOnlyList<string> Phases,
    IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedTransitions,
    IReadOnlyDictionary<string, ActionDefinition> Actions);
