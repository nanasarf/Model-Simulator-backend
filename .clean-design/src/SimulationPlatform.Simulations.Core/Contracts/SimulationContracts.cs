using System.Text.Json;

namespace SimulationPlatform.Simulations.Core.Contracts;

public sealed record SimulationModelDescriptor(string Identifier, string Version, string Name);
public sealed record InitializationContext(JsonElement Configuration, int Seed);
public sealed record ActionValidationContext(JsonElement State, string ActionCode, JsonElement Payload);
public sealed record RoundAction(string Code, JsonElement Payload);
public sealed record RoundExecutionContext(JsonElement State, IReadOnlyList<RoundAction> Actions, int Seed, int RoundNumber);
public sealed record StateProjectionContext(JsonElement State, IReadOnlySet<string> Capabilities);
public sealed record ActionValidationResult(bool IsValid, string? ErrorCode = null, string? Message = null);
public sealed record RoundExecutionResult(JsonElement State, IReadOnlyDictionary<string, decimal> Metrics);

public interface ISimulationModel
{
    SimulationModelDescriptor Descriptor { get; }
    ValueTask<JsonElement> InitializeAsync(InitializationContext context, CancellationToken cancellationToken);
    ValueTask<ActionValidationResult> ValidateActionAsync(ActionValidationContext context, CancellationToken cancellationToken);
    ValueTask<RoundExecutionResult> ExecuteRoundAsync(RoundExecutionContext context, CancellationToken cancellationToken);
    ValueTask<JsonElement> GenerateVisibleStateAsync(StateProjectionContext context, CancellationToken cancellationToken);
}

public interface ISimulationModelRegistry
{
    ISimulationModel Resolve(string identifier, string version);
}
