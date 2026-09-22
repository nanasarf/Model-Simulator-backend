using SimulationPlatform.Application.Abstractions;

namespace Tests.Unit;

public sealed class IdempotencyRequestHasherTests
{
    private readonly IIdempotencyRequestHasher _hasher = new IdempotencyRequestHasher();
    private static readonly Guid Resource = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Same_logical_request_has_same_hash()
        => Assert.Equal(_hasher.Hash("scenario_proposal.approve", Resource, 7),
            _hasher.Hash("scenario_proposal.approve", Resource, 7));

    [Fact]
    public void Operation_resource_and_version_are_hash_inputs()
    {
        var baseline = _hasher.Hash("scenario_proposal.approve", Resource, 7);
        Assert.NotEqual(baseline, _hasher.Hash("scenario_proposal.reject", Resource, 7));
        Assert.NotEqual(baseline, _hasher.Hash("scenario_proposal.approve", Guid.NewGuid(), 7));
        Assert.NotEqual(baseline, _hasher.Hash("scenario_proposal.approve", Resource, 8));
    }

    [Fact]
    public void Hash_is_culture_independent()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var first = _hasher.Hash("scenario_proposal.approve", Resource, 7);
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal(first, _hasher.Hash("scenario_proposal.approve", Resource, 7));
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }
}
