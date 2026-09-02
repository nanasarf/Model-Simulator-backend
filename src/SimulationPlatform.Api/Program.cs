using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Application.Actions;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Definitions;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Infrastructure.InMemory;
using SimulationPlatform.Simulations.Core.Contracts;
using SimulationPlatform.Simulations.Economics.SupplyDemand;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();
builder.Services.AddSingleton<SupplyDemandModel>();
builder.Services.AddSingleton<ISimulationModel>(sp => sp.GetRequiredService<SupplyDemandModel>());
builder.Services.AddSingleton<ISimulationModelRegistry, SimulationModelRegistry>();
builder.Services.AddSingleton<InMemoryRuntimeStore>();
builder.Services.AddSingleton<IRuntimeStore>(sp => sp.GetRequiredService<InMemoryRuntimeStore>());
builder.Services.AddSingleton<IScenarioCatalog>(sp => sp.GetRequiredService<InMemoryRuntimeStore>());
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<SubmitActionHandler>();

var app = builder.Build();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, code, detail) = error switch
    {
        DomainException domain when domain.Code is "session.not_found" or "scenario.not_found" =>
            (StatusCodes.Status404NotFound, domain.Code, domain.Message),
        DomainException domain when domain.Code == "idempotency.conflict" =>
            (StatusCodes.Status409Conflict, domain.Code, domain.Message),
        DomainException domain => (StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message),
        _ => (StatusCodes.Status500InternalServerError, "server.error", "An unexpected error occurred.")
    };
    context.Response.StatusCode = status;
    await Results.Problem(statusCode: status, title: code, detail: detail,
        extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
}));

app.MapHealthChecks("/health");
app.MapGet("/api/v1/models", (IEnumerable<ISimulationModel> models) => models.Select(x => x.Descriptor));
app.MapPost("/api/v1/sessions/{sessionId:guid}/actions", async (Guid sessionId, SubmitActionRequest request,
    HttpRequest httpRequest, SubmitActionHandler handler, CancellationToken cancellationToken) =>
{
    var result = await handler.HandleAsync(new(sessionId, request.TeamId, request.UserId, request.RoleAssignmentId,
        request.ActionCode, request.Payload, httpRequest.Headers["Idempotency-Key"].ToString()), cancellationToken);
    return Results.Accepted($"/api/v1/sessions/{sessionId}/actions/{result.Id}", result);
});
app.MapHub<SessionHub>("/hubs/sessions");
SeedDevelopmentDemo(app.Services);
app.Run();

static void SeedDevelopmentDemo(IServiceProvider services)
{
    var store = services.GetRequiredService<InMemoryRuntimeStore>();
    var model = services.GetRequiredService<SupplyDemandModel>();
    var action = new ActionDefinition(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "CHANGE_OUTPUT",
        "CHANGE_PRODUCTION", new HashSet<string> { SessionPhases.Decision });
    var scenario = new ScenarioVersion(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "Coffee Market", 1,
        model.Descriptor.Identifier, model.Descriptor.Version,
        [SessionPhases.Briefing, SessionPhases.Decision, SessionPhases.Locked, SessionPhases.Simulation, SessionPhases.Results],
        new Dictionary<string, IReadOnlySet<string>> {
            [SessionPhases.Briefing] = new HashSet<string> { SessionPhases.Decision },
            [SessionPhases.Decision] = new HashSet<string> { SessionPhases.Locked },
            [SessionPhases.Locked] = new HashSet<string> { SessionPhases.Simulation },
            [SessionPhases.Simulation] = new HashSet<string> { SessionPhases.Results } },
        new Dictionary<string, ActionDefinition> { [action.Code] = action });
    store.Scenarios[scenario.Id] = scenario;
}

public sealed record SubmitActionRequest(Guid TeamId, Guid UserId, Guid RoleAssignmentId, string ActionCode, JsonElement Payload);
public sealed class SessionHub : Microsoft.AspNetCore.SignalR.Hub;
public partial class Program;
