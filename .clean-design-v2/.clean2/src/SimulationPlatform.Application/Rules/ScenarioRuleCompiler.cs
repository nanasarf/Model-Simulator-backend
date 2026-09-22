using System.Text.Json;
using SimulationPlatform.Simulations.Core.Authoring;

namespace SimulationPlatform.Application.Rules;

public sealed record RuleCompilationIssue(string Code,string Message,string RuleId,string Path,string Severity);
public sealed record RuleCompilationResult(bool IsValid, RuleDefinition? CompiledRule, IReadOnlyList<RuleCompilationIssue> Blockers, IReadOnlyList<RuleCompilationIssue> Warnings);
public interface IScenarioRuleCompiler { RuleCompilationResult Compile(RuleBlueprint rule, AuthoringCatalog catalog, IReadOnlySet<string> objectives); }
public sealed class ScenarioRuleCompiler : IScenarioRuleCompiler
{
    public RuleCompilationResult Compile(RuleBlueprint rule, AuthoringCatalog catalog, IReadOnlySet<string> objectives)
    {
        var errors=new List<RuleCompilationIssue>(); if(!catalog.Actions.Contains(rule.Trigger))errors.Add(new("scenario_blueprint.unsupported_action","Trigger action is unsupported.",rule.Id,"trigger","Blocker")); foreach(var o in rule.LearningObjectiveCodes)if(!objectives.Contains(o))errors.Add(new("scenario_blueprint.objective_unknown","Learning objective is unknown.",rule.Id,"learningObjectiveCodes","Blocker"));
        RuleNode? node=null; try { node=Parse(rule.Conditions, catalog, rule.Id, errors); } catch { errors.Add(new("scenario_blueprint.invalid_rule","Rule condition is invalid.",rule.Id,"conditions","Blocker")); }
        return errors.Count>0?new(false,null,errors,[]):new(true,new RuleDefinition(Guid.TryParse(rule.Id,out var id)?id:Guid.NewGuid(),0,RuleEffect.Allow,node!),[],[]);
    }
    private static RuleNode? Parse(JsonElement json, AuthoringCatalog catalog,string id,List<RuleCompilationIssue> errors)
    { if(json.ValueKind==JsonValueKind.Undefined||json.ValueKind==JsonValueKind.Null)return new ExistsRule("action.code"); if(!json.TryGetProperty("kind",out var k))throw new InvalidOperationException(); var kind=k.GetString(); if(kind=="exists")return new ExistsRule(json.GetProperty("fact").GetString()!); if(kind=="comparison"){var fact=json.GetProperty("fact").GetString()!; if(catalog.StateVariables is null||!catalog.StateVariables.Contains(fact))errors.Add(new("scenario_blueprint.unsupported_variable","Condition variable is unsupported.",id,"conditions.fact","Blocker")); var op=json.GetProperty("operator").GetString()!; var map=new Dictionary<string,ComparisonOperator>{{"eq",ComparisonOperator.Eq},{"neq",ComparisonOperator.Neq},{"gt",ComparisonOperator.Gt},{"gte",ComparisonOperator.Gte},{"lt",ComparisonOperator.Lt},{"lte",ComparisonOperator.Lte}}; if(!map.TryGetValue(op,out var cmp)){errors.Add(new("scenario_blueprint.unsupported_operator","Rule operator is unsupported.",id,"conditions.operator","Blocker"));return null;} return new ComparisonRule(fact,cmp,json.GetProperty("value"));} throw new InvalidOperationException(); }
}
