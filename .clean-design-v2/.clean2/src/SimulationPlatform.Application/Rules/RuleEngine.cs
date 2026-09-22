using System.Text.Json;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Application.Rules;

public enum RuleEffect { Allow, Deny }
public enum ComparisonOperator { Eq, Neq, Gt, Gte, Lt, Lte }

public abstract record RuleNode;
public sealed record AllRule(IReadOnlyList<RuleNode> Children) : RuleNode;
public sealed record AnyRule(IReadOnlyList<RuleNode> Children) : RuleNode;
public sealed record NotRule(RuleNode Child) : RuleNode;
public sealed record ExistsRule(string Fact) : RuleNode;
public sealed record ContainsRule(string Fact, JsonElement Value) : RuleNode;
public sealed record ComparisonRule(string Fact, ComparisonOperator Operator, JsonElement Value) : RuleNode;
public sealed record RuleDefinition(Guid Id, int Priority, RuleEffect Effect, RuleNode Condition);

public interface IRuleFacts
{
    bool TryGet(string path, out object? value);
}

public sealed record RuleDecision(bool Allowed, Guid? DecidingRuleId, string Reason);

public sealed class RuleEngine
{
    public const int MaxDepth = 12;
    public const int MaxNodes = 128;

    public RuleDecision Evaluate(IEnumerable<RuleDefinition> rules, IRuleFacts facts, bool defaultAllow = false)
    {
        var ordered = rules.OrderByDescending(x => x.Priority).ThenBy(x => x.Effect == RuleEffect.Deny ? 0 : 1).ThenBy(x => x.Id);
        foreach (var rule in ordered)
        {
            var budget = MaxNodes;
            if (EvaluateNode(rule.Condition, facts, 0, ref budget))
                return new(rule.Effect == RuleEffect.Allow, rule.Id, rule.Effect == RuleEffect.Allow ? "allowed_by_rule" : "denied_by_rule");
        }
        return new(defaultAllow, null, defaultAllow ? "default_allow" : "default_deny");
    }

    private static bool EvaluateNode(RuleNode node, IRuleFacts facts, int depth, ref int budget)
    {
        if (depth > MaxDepth || --budget < 0) throw new DomainException("rule.complexity_exceeded", "Rule evaluation limits were exceeded.");
        return node switch
        {
            AllRule all => EvaluateAll(all.Children, facts, depth, ref budget),
            AnyRule any => EvaluateAny(any.Children, facts, depth, ref budget),
            NotRule not => !EvaluateNode(not.Child, facts, depth + 1, ref budget),
            ExistsRule exists => facts.TryGet(exists.Fact, out _),
            ContainsRule contains => Contains(ReadFact(facts, contains.Fact), ToValue(contains.Value)),
            ComparisonRule comparison => Compare(ReadFact(facts, comparison.Fact), ToValue(comparison.Value), comparison.Operator),
            _ => throw new DomainException("rule.node_unknown", "Unknown rule node.")
        };
    }

    private static bool EvaluateAll(IReadOnlyList<RuleNode> nodes, IRuleFacts facts, int depth, ref int budget)
    {
        if (nodes.Count == 0) return false;
        foreach (var node in nodes) if (!EvaluateNode(node, facts, depth + 1, ref budget)) return false;
        return true;
    }

    private static bool EvaluateAny(IReadOnlyList<RuleNode> nodes, IRuleFacts facts, int depth, ref int budget)
    {
        foreach (var node in nodes) if (EvaluateNode(node, facts, depth + 1, ref budget)) return true;
        return false;
    }

    private static object? ReadFact(IRuleFacts facts, string path) => facts.TryGet(path, out var value)
        ? value : throw new DomainException("rule.fact_unknown", $"Fact '{path}' is not registered.");

    private static object? ToValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(), JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => null,
        _ => throw new DomainException("rule.value_invalid", "Rule values must be scalar.")
    };

    private static bool Contains(object? actual, object? expected) => actual switch
    {
        IEnumerable<string> strings when expected is string value => strings.Contains(value, StringComparer.Ordinal),
        string text when expected is string value => text.Contains(value, StringComparison.Ordinal),
        _ => throw new DomainException("rule.type_mismatch", "Contains requires a string or string collection.")
    };

    private static bool Compare(object? actual, object? expected, ComparisonOperator op)
    {
        if (actual is JsonElement element) actual = ToValue(element);
        if (actual is IConvertible && expected is decimal) actual = Convert.ToDecimal(actual);
        var comparison = actual switch
        {
            decimal left when expected is decimal right => left.CompareTo(right),
            string left when expected is string right => string.CompareOrdinal(left, right),
            bool left when expected is bool right => left.CompareTo(right),
            null when expected is null => 0,
            _ => throw new DomainException("rule.type_mismatch", "Rule comparison types do not match.")
        };
        return op switch { ComparisonOperator.Eq => comparison == 0, ComparisonOperator.Neq => comparison != 0, ComparisonOperator.Gt => comparison > 0, ComparisonOperator.Gte => comparison >= 0, ComparisonOperator.Lt => comparison < 0, ComparisonOperator.Lte => comparison <= 0, _ => false };
    }
}

public sealed class DictionaryRuleFacts(IReadOnlyDictionary<string, object?> values) : IRuleFacts
{
    public bool TryGet(string path, out object? value) => values.TryGetValue(path, out value);
}
