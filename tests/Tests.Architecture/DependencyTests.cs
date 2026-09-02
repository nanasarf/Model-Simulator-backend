namespace Tests.Architecture;

public sealed class DependencyTests
{
    [Fact]
    public void Domain_has_no_reference_to_economics()
    {
        var references = typeof(SimulationPlatform.Domain.Runtime.SimulationSession).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, x => x.Name == "SimulationPlatform.Simulations.Economics");
    }
}
