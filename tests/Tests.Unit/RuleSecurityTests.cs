using System.Text.Json;
using SimulationPlatform.Application.Rules;
using SimulationPlatform.Domain.Common;

namespace Tests.Unit;

public sealed class RuleSecurityTests
{
    [Fact]
    public void Unknown_fact_fails_closed()
    {
        var rule = new RuleDefinition(Guid.NewGuid(), 1, RuleEffect.Allow,
            new ExistsRule("state.__proto__.secret"));
        var result = new RuleEngine().Evaluate([rule], new DictionaryRuleFacts(new Dictionary<string, object?>()));
        Assert.False(result.Allowed);
    }

    [Fact]
    public void Unknown_fact_in_comparison_is_rejected()
    {
        var rule = new RuleDefinition(Guid.NewGuid(), 1, RuleEffect.Allow,
            new ComparisonRule("environment.connectionString", ComparisonOperator.Eq, JsonSerializer.SerializeToElement("stolen")));
        var error = Assert.Throws<DomainException>(() => new RuleEngine().Evaluate([rule], new DictionaryRuleFacts(new Dictionary<string, object?>())));
        Assert.Equal("rule.fact_unknown", error.Code);
    }

    [Fact]
    public void Deny_wins_at_equal_priority()
    {
        var facts = new DictionaryRuleFacts(new Dictionary<string, object?> { ["runtime.phase"] = "Decision" });
        var condition = new ComparisonRule("runtime.phase", ComparisonOperator.Eq, JsonSerializer.SerializeToElement("Decision"));
        var result = new RuleEngine().Evaluate([
            new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 10, RuleEffect.Allow, condition),
            new(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 10, RuleEffect.Deny, condition)], facts);
        Assert.False(result.Allowed);
        Assert.Equal("denied_by_rule", result.Reason);
    }

    [Fact]
    public void Excessive_ast_depth_is_rejected()
    {
        RuleNode node = new ExistsRule("known");
        for (var index = 0; index < RuleEngine.MaxDepth + 2; index++) node = new NotRule(node);
        var error = Assert.Throws<DomainException>(() => new RuleEngine().Evaluate(
            [new(Guid.NewGuid(), 1, RuleEffect.Allow, node)], new DictionaryRuleFacts(new Dictionary<string, object?> { ["known"] = true })));
        Assert.Equal("rule.complexity_exceeded", error.Code);
    }
}
