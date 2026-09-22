using System.Text.Json;
using SimulationPlatform.Application.Rules;
using SimulationPlatform.Simulations.Core.Authoring;
using SimulationPlatform.Simulations.Economics.Macroeconomics;
using SimulationPlatform.Simulations.Economics.CompetitiveMarket;

namespace SimulationPlatform.Simulations.Economics.Authoring;

public sealed record BlueprintAdaptationIssue(string Code,string Message,string Path,string Severity);
public sealed record BlueprintAdaptationResult<T>(bool IsValid,T? Content,IReadOnlyList<BlueprintAdaptationIssue> Blockers,IReadOnlyList<BlueprintAdaptationIssue> Warnings);
public interface IScenarioBlueprintAdapter { string ModelIdentifier {get;} string ModelVersion {get;} BlueprintAdaptationResult<object> Adapt(ScenarioBlueprint blueprint,IReadOnlyList<RuleDefinition> rules); }
public interface IScenarioBlueprintAdapterRegistry { bool TryResolve(string identifier,string version,out IScenarioBlueprintAdapter adapter); }
public sealed class ScenarioBlueprintAdapterRegistry(IEnumerable<IScenarioBlueprintAdapter> adapters):IScenarioBlueprintAdapterRegistry
{ readonly Dictionary<string,IScenarioBlueprintAdapter> map=adapters.ToDictionary(x=>$"{x.ModelIdentifier}:{x.ModelVersion}"); public bool TryResolve(string i,string v,out IScenarioBlueprintAdapter a)=>map.TryGetValue($"{i}:{v}",out a!); }
public sealed class ShortRunMacroBlueprintAdapter:IScenarioBlueprintAdapter
{
 public string ModelIdentifier=>"Economics.ShortRunMacro"; public string ModelVersion=>"1.0.0";
 public BlueprintAdaptationResult<object> Adapt(ScenarioBlueprint b,IReadOnlyList<RuleDefinition> rules){var issues=new List<BlueprintAdaptationIssue>();var roles=b.Roles.Select(r=>new MacroRoleAuthoring(r.Code,true,true,r.Responsibilities.ToList())).ToList();var actions=b.Decisions.Select(x=>x.Code).ToHashSet();foreach(var r in roles)if(r.Code is not("GOVERNMENT" or "CENTRAL_BANK" or "BUSINESS" or "HOUSEHOLD_LABOR"))issues.Add(new("unsupported_role",$"Role {r.Code} is not supported.","roles","Blocker"));var content=new MacroScenarioContent(b.StudentBriefing,b.LearningObjectives.Select(x=>x.Description).ToList(),[],[],new(),b.RecommendedRounds,roles,actions,[PolicyIntensity.Mild,PolicyIntensity.Moderate,PolicyIntensity.Strong],[],new());return new(issues.Count==0,content,issues,[]);}
}
public sealed class CompetitiveMarketBlueprintAdapter:IScenarioBlueprintAdapter
{
 public string ModelIdentifier=>"Economics.CompetitiveMarket"; public string ModelVersion=>"1.0.0";
 public BlueprintAdaptationResult<object> Adapt(ScenarioBlueprint b,IReadOnlyList<RuleDefinition> rules){var issues=new List<BlueprintAdaptationIssue>();var roles=b.Roles.Select(r=>new CompetitiveMarketRole(r.Code,true,true,r.Responsibilities.ToList())).ToList();var actions=b.Decisions.Select(x=>x.Code).ToHashSet();foreach(var r in roles)if(r.Code is not("BUYER" or "SELLER" or "GOVERNMENT"))issues.Add(new("unsupported_role",$"Role {r.Code} is not supported.","roles","Blocker"));var content=new CompetitiveMarketScenarioContent(b.StudentBriefing,b.LearningObjectives.Select(x=>x.Description).ToList(),[],[],new(),b.RecommendedRounds,roles,actions);return new(issues.Count==0,content,issues,[]);}
}
