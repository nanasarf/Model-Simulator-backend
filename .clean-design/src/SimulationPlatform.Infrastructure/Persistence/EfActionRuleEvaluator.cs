using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Application.Rules;
using SimulationPlatform.Domain.Common;

namespace SimulationPlatform.Infrastructure.Persistence;

public sealed class EfActionRuleEvaluator(PlatformDbContext db, RuleEngine engine) : IActionRuleEvaluator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async ValueTask<bool> IsAllowedAsync(ActionRuleContext context, CancellationToken ct)
    {
        var manifestJson = await db.SessionManifests.AsNoTracking()
            .Where(x => x.SessionId == context.SessionId)
            .Select(x => x.ManifestJson)
            .SingleOrDefaultAsync(ct)
            ?? throw new DomainException("session.manifest_missing", "The frozen session manifest was not found.");
        var manifest = JsonSerializer.Deserialize<SimulationPlatform.Application.Classrooms.ScenarioManifest>(manifestJson, JsonOptions)
            ?? throw new DomainException("session.manifest_invalid", "The frozen session manifest is invalid.");
        if (manifest.Rules.Count == 0) return true;
        var definitions = manifest.Rules.Select(x => new RuleDefinition(x.Id, x.Priority,
            x.Effect == "Deny" ? RuleEffect.Deny : x.Effect == "Allow" ? RuleEffect.Allow
                : throw new DomainException("rule.effect_unknown", "Unknown rule effect."), RuleAstParser.Parse(x.Condition.GetRawText())));
        var facts = new DictionaryRuleFacts(new Dictionary<string, object?> {
            ["runtime.phase"] = context.Phase, ["actor.capabilities"] = context.Capabilities,
            ["team.id"] = context.TeamId.ToString(), ["action.code"] = context.ActionCode,
            ["submission.count"] = context.SubmissionCount });
        return engine.Evaluate(definitions, facts).Allowed;
    }
}

internal static class RuleAstParser
{
    public static RuleNode Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = RuleEngine.MaxDepth + 2 });
        var budget = RuleEngine.MaxNodes;
        return ParseNode(document.RootElement, 0, ref budget);
    }

    private static RuleNode ParseNode(JsonElement element, int depth, ref int budget)
    {
        if (depth > RuleEngine.MaxDepth || --budget < 0) throw new DomainException("rule.complexity_exceeded", "Rule AST limits were exceeded.");
        var kind = element.GetProperty("kind").GetString();
        return kind switch
        {
            "all" => new AllRule(ParseChildren(element, depth, ref budget)),
            "any" => new AnyRule(ParseChildren(element, depth, ref budget)),
            "not" => new NotRule(ParseNode(element.GetProperty("child"), depth + 1, ref budget)),
            "exists" => new ExistsRule(Fact(element)),
            "contains" => new ContainsRule(Fact(element), Scalar(element.GetProperty("value"))),
            "comparison" => new ComparisonRule(Fact(element), Operator(element.GetProperty("operator").GetString()), Scalar(element.GetProperty("value"))),
            _ => throw new DomainException("rule.node_unknown", "Unknown rule AST node.")
        };
    }

    private static IReadOnlyList<RuleNode> ParseChildren(JsonElement element, int depth, ref int budget)
    {
        var result = new List<RuleNode>();
        foreach (var child in element.GetProperty("children").EnumerateArray()) result.Add(ParseNode(child, depth + 1, ref budget));
        return result;
    }

    private static string Fact(JsonElement element)
    {
        var fact = element.GetProperty("fact").GetString() ?? "";
        var allowed = fact is "runtime.phase" or "actor.capabilities" or "team.id" or "action.code" or "submission.count";
        if (!allowed || fact.Length > 100) throw new DomainException("rule.fact_unknown", "Rule fact is not registered.");
        return fact;
    }

    private static JsonElement Scalar(JsonElement value) => value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null
        ? value.Clone() : throw new DomainException("rule.value_invalid", "Rule values must be scalar.");

    private static ComparisonOperator Operator(string? value) => value switch
    {
        "eq" => ComparisonOperator.Eq, "neq" => ComparisonOperator.Neq, "gt" => ComparisonOperator.Gt,
        "gte" => ComparisonOperator.Gte, "lt" => ComparisonOperator.Lt, "lte" => ComparisonOperator.Lte,
        _ => throw new DomainException("rule.operator_unknown", "Unknown rule operator.")
    };
}
