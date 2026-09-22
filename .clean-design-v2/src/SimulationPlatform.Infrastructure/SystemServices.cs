using SimulationPlatform.Domain.Common;
using SimulationPlatform.Simulations.Core.Contracts;

namespace SimulationPlatform.Infrastructure;

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }

public sealed class SimulationModelRegistry(IEnumerable<ISimulationModel> models) : ISimulationModelRegistry
{
    private readonly Dictionary<(string, string), ISimulationModel> _models = models.ToDictionary(x => (x.Descriptor.Identifier, x.Descriptor.Version));
    public ISimulationModel Resolve(string identifier, string version) => _models.TryGetValue((identifier, version), out var model)
        ? model : throw new DomainException("model.not_found", $"Simulation model {identifier}:{version} is not registered.");
}
