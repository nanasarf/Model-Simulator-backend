namespace Tests.Architecture;

public sealed class DependencyTests
{
    [Fact]
    public void Domain_has_no_reference_to_economics()
    {
        var references = typeof(SimulationPlatform.Domain.Runtime.SimulationSession).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, x => x.Name == "SimulationPlatform.Simulations.Economics");
    }

    [Theory]
    [InlineData(typeof(SimulationPlatform.Domain.Runtime.SimulationSession))]
    [InlineData(typeof(SimulationPlatform.Application.Actions.SubmitActionHandler))]
    [InlineData(typeof(SimulationPlatform.Identity.ApplicationUser))]
    [InlineData(typeof(SimulationPlatform.Infrastructure.Persistence.PlatformDbContext))]
    public void Core_layers_have_no_economics_reference(Type marker)
    {
        Assert.DoesNotContain(marker.Assembly.GetReferencedAssemblies(),
            reference => reference.Name == "SimulationPlatform.Simulations.Economics");
    }
}
