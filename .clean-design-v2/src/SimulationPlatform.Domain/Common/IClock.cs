namespace SimulationPlatform.Domain.Common;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
