using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using SimulationPlatform.Application.Abstractions;
using SimulationPlatform.Application.Actions;
using SimulationPlatform.Application.Runtime;
using SimulationPlatform.Application.Rules;
using SimulationPlatform.Application.Classrooms;
using SimulationPlatform.Api;
using SimulationPlatform.Domain.Common;
using SimulationPlatform.Domain.Definitions;
using SimulationPlatform.Domain.Runtime;
using SimulationPlatform.Infrastructure.Persistence;
using SimulationPlatform.Infrastructure;
using SimulationPlatform.Infrastructure.Outbox;
using SimulationPlatform.Identity;
using SimulationPlatform.Identity.Authorization;
using SimulationPlatform.Simulations.Core.Contracts;
using SimulationPlatform.Simulations.Core.Authoring;
using SimulationPlatform.Simulations.Economics.SupplyDemand;
using SimulationPlatform.Simulations.Economics.Macroeconomics;
using SimulationPlatform.Simulations.Economics.Authoring;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Logging.AddDebug();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddHealthChecks();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Simulation Platform API",
        Version = "v1",
        Description = "Backend API for the multiplayer educational simulation platform."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Enter the JWT access token returned by /api/v1/auth/login."
    });
});
var connectionString = builder.Configuration.GetConnectionString("Platform")
    ?? throw new InvalidOperationException("ConnectionStrings:Platform is required.");
var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()
    ?? throw new InvalidOperationException($"{JwtOptions.Section} configuration is required.");
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32) throw new InvalidOperationException("JWT signing key must be at least 256 bits.");
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.Section));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddPooledDbContextFactory<PlatformDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContext());
builder.Services.AddDbContext<IdentityDataContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
    .AddRoles<ApplicationRole>().AddEntityFrameworkStores<IdentityDataContext>().AddDefaultTokenProviders();
builder.Services.AddAuthentication().AddJwtBearer(options =>
{
    options.TokenValidationParameters = new() { ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience, ValidateLifetime = true,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ClockSkew = TimeSpan.FromSeconds(30), NameClaimType = ClaimTypes.NameIdentifier, RoleClaimType = ClaimTypes.Role };
    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var token = context.Request.Query["access_token"];
            if (!string.IsNullOrWhiteSpace(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/sessions"))
                context.Token = token;
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var subject = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Principal?.FindFirstValue("sub");
            var stamp = context.Principal?.FindFirstValue("security_stamp");
            var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            if (!Guid.TryParse(subject, out var userId)) { context.Fail("Missing subject."); return; }
            var user = await users.FindByIdAsync(userId.ToString());
            if (user is null || !user.IsActive || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(stamp ?? ""), Encoding.UTF8.GetBytes(user.SecurityStamp ?? "")))
                context.Fail("Account security state changed.");
        },
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
            logger.LogWarning(context.Exception, "JWT authentication failed for {Path}; trace {TraceId}", context.Request.Path, context.HttpContext.TraceIdentifier);
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
            logger.LogDebug("JWT challenge {Error} for {Path}; trace {TraceId}", context.Error, context.Request.Path, context.HttpContext.TraceIdentifier);
            return Task.CompletedTask;
        },
        OnForbidden = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("JwtBearer");
            logger.LogInformation("JWT authorization forbidden for {Path}; trace {TraceId}", context.Request.Path, context.HttpContext.TraceIdentifier);
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PlatformPolicies.Instructor, policy => policy.RequireRole(PlatformRoles.Instructor, PlatformRoles.Administrator));
    options.AddPolicy(PlatformPolicies.Student, policy => policy.RequireRole(PlatformRoles.Student));
    options.AddPolicy(PlatformPolicies.Administrator, policy => policy.RequireRole(PlatformRoles.Administrator));
});
builder.Services.AddSingleton<IAuthorizationHandler, ManageOwnedResourceHandler>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddHostedService<DatabaseInitializer>();
builder.Services.AddSingleton<IIntegrationEventPublisher, SignalRIntegrationEventPublisher>();
builder.Services.AddHostedService<OutboxDispatcher>();
builder.Services.AddSingleton<SupplyDemandModel>();
builder.Services.AddSingleton<ISimulationModel>(sp => sp.GetRequiredService<SupplyDemandModel>());
builder.Services.AddSingleton<ShortRunMacroModel>();
builder.Services.AddSingleton<ISimulationModel>(sp => sp.GetRequiredService<ShortRunMacroModel>());
builder.Services.AddSingleton<SimulationPlatform.Simulations.Economics.CompetitiveMarket.CompetitiveMarketModel>();
builder.Services.AddSingleton<ISimulationModel>(sp => sp.GetRequiredService<SimulationPlatform.Simulations.Economics.CompetitiveMarket.CompetitiveMarketModel>());
builder.Services.AddSingleton<ISimulationModelRegistry, SimulationModelRegistry>();
builder.Services.AddSingleton<IAuthoringCapabilityCatalog, DefaultAuthoringCapabilityCatalog>();
builder.Services.AddSingleton<IScenarioBlueprintAdapter, ShortRunMacroBlueprintAdapter>();
builder.Services.AddSingleton<IScenarioBlueprintAdapter, CompetitiveMarketBlueprintAdapter>();
builder.Services.AddSingleton<IScenarioBlueprintAdapterRegistry, ScenarioBlueprintAdapterRegistry>();
builder.Services.AddScoped<ScenarioBlueprintValidator>();
builder.Services.AddSingleton<IScenarioRuleCompiler, ScenarioRuleCompiler>();
builder.Services.AddScoped<EfRuntimeStore>();
builder.Services.AddScoped<IRuntimeStore>(sp => sp.GetRequiredService<EfRuntimeStore>());
builder.Services.AddScoped<IScenarioCatalog>(sp => sp.GetRequiredService<EfRuntimeStore>());
builder.Services.AddScoped<ITransactionRunner, EfTransactionRunner>();
builder.Services.AddScoped<IRoundExecutionStore>(sp => sp.GetRequiredService<EfRuntimeStore>());
builder.Services.AddScoped<IActionRuleEvaluator, EfActionRuleEvaluator>();
builder.Services.AddSingleton<RuleEngine>();
builder.Services.AddScoped<IAuditWriter, EfAuditWriter>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IIdempotencyRequestHasher, IdempotencyRequestHasher>();
builder.Services.AddSingleton<IProposalCommandFaultInjector, NoOpProposalCommandFaultInjector>();
builder.Services.AddScoped<IScenarioProposalApprovalService, ScenarioProposalApprovalService>();
builder.Services.AddScoped<IScenarioProposalRejectionService, ScenarioProposalRejectionService>();
builder.Services.AddScoped<SubmitActionHandler>();
builder.Services.AddScoped<ExecuteRoundHandler>();
builder.Services.AddScoped<IClassroomWorkflow, EfClassroomWorkflow>();
builder.Services.AddScoped<IScenarioDraftStore, EfScenarioDraftStore>();
builder.Services.AddScoped<IMacroClassroomGameplay, MacroClassroomGameplay>();
builder.Services.AddScoped<IMacroScenarioAuthoring, MacroScenarioAuthoring>();
builder.Services.AddScoped<SimulationPlatform.Application.Assessment.IAssessmentCommentStore, EfAssessmentCommentStore>();
builder.Services.AddScoped<IMacroLearningAnalytics, MacroLearningAnalytics>();
builder.Services.AddScoped<SimulationPlatform.Simulations.Economics.CompetitiveMarket.ICompetitiveMarketAuthoring, SimulationPlatform.Simulations.Economics.CompetitiveMarket.CompetitiveMarketAuthoring>();
builder.Services.AddScoped<SimulationPlatform.Simulations.Economics.CompetitiveMarket.ICompetitiveMarketGameplay, SimulationPlatform.Simulations.Economics.CompetitiveMarket.CompetitiveMarketGameplay>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Simulation Platform API v1");
        options.RoutePrefix = "swagger";
        options.DisplayRequestDuration();
    });
}
app.UseAuthentication();
app.UseAuthorization();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, code, detail) = error switch
    {
        DomainException domain when domain.Code.EndsWith(".not_found", StringComparison.Ordinal) =>
            (StatusCodes.Status404NotFound, domain.Code, domain.Message),
        DomainException domain when domain.Code is "idempotency.conflict" or "round.already_executed" or
            "session.status_conflict" or "session.frozen" or "role.capacity_reached" =>
            (StatusCodes.Status409Conflict, domain.Code, domain.Message),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "concurrency.conflict", "The resource changed; reload and retry."),
        DbUpdateException dbUpdate when dbUpdate.InnerException is Npgsql.PostgresException { SqlState: "23505" } =>
            (StatusCodes.Status409Conflict, "persistence.duplicate", "The operation conflicts with an existing resource."),
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "authentication.required", "Authentication is required."),
        SecurityTokenException => (StatusCodes.Status401Unauthorized, "authentication.invalid_token", "The supplied token is invalid."),
        DomainException domain => (StatusCodes.Status422UnprocessableEntity, domain.Code, domain.Message),
        _ => (StatusCodes.Status500InternalServerError, "server.error", "An unexpected error occurred.")
    };
    context.Response.StatusCode = status;
    await Results.Problem(statusCode: status, title: code, detail: detail,
        extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
}));

app.MapHealthChecks("/health");
app.MapPost("/api/v1/auth/register", async (RegisterRequest request, UserManager<ApplicationUser> users, IClock clock, PlatformDbContext auditDb, HttpContext http) =>
{
    var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = request.Email.Trim(), Email = request.Email.Trim(), CreatedAt = clock.UtcNow };
    var created = await users.CreateAsync(user, request.Password);
    if (!created.Succeeded) return Results.ValidationProblem(created.Errors.GroupBy(x => x.Code).ToDictionary(x => x.Key, x => x.Select(e => e.Description).ToArray()));
    var roleAdded = await users.AddToRoleAsync(user, PlatformRoles.Student);
    if (!roleAdded.Succeeded) return Results.Problem(statusCode: 500, title: "identity.role_assignment_failed");
    auditDb.AuditRecords.Add(AuditFactory.Create(user.Id, "Identity.Registered", "User", user.Id.ToString(), http.TraceIdentifier, clock.UtcNow));
    await auditDb.SaveChangesAsync();
    return Results.Created($"/api/v1/users/{user.Id}", new { user.Id, user.Email });
});
app.MapPost("/api/v1/auth/login", async (LoginRequest request, UserManager<ApplicationUser> users, ITokenService tokens,
    PlatformDbContext auditDb, IClock clock, HttpContext http, CancellationToken ct) =>
{
    var user = await users.FindByEmailAsync(request.Email.Trim());
    if (user is null || !user.IsActive || !await users.CheckPasswordAsync(user, request.Password))
    {
        auditDb.AuditRecords.Add(AuditFactory.Create(user?.Id, "Identity.LoginFailed", "User", user?.Id.ToString() ?? "unknown", http.TraceIdentifier, clock.UtcNow));
        await auditDb.SaveChangesAsync(ct);
        return Results.Problem(statusCode: 401, title: "authentication.invalid_credentials");
    }
    var roles = await users.GetRolesAsync(user);
    auditDb.AuditRecords.Add(AuditFactory.Create(user.Id, "Identity.LoginSucceeded", "User", user.Id.ToString(), http.TraceIdentifier, clock.UtcNow));
    await auditDb.SaveChangesAsync(ct);
    return Results.Ok(await tokens.IssueAsync(user, roles.ToArray(), ct));
});
app.MapPost("/api/v1/auth/refresh", async (RefreshRequest request, ITokenService tokens, CancellationToken ct) =>
    Results.Ok(await tokens.RotateAsync(request.RefreshToken, ct)));
app.MapPost("/api/v1/auth/logout", async (RefreshRequest request, ITokenService tokens, CancellationToken ct) =>
{
    await tokens.RevokeAsync(request.RefreshToken, "logout", ct);
    return Results.NoContent();
});
app.MapGet("/api/v1/models", (IEnumerable<ISimulationModel> models) => models.Select(x => x.Descriptor));
app.MapPost("/api/v1/sessions/{sessionId:guid}/actions", async (Guid sessionId, SubmitActionRequest request,
    ClaimsPrincipal principal, HttpRequest httpRequest, SubmitActionHandler handler, CancellationToken cancellationToken) =>
{
    var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
    if (!Guid.TryParse(subject, out var userId)) return Results.Unauthorized();
    var result = await handler.HandleAsync(new(sessionId, request.TeamId, userId, request.RoleAssignmentId,
        request.ActionCode, request.Payload, httpRequest.Headers["Idempotency-Key"].ToString()), cancellationToken);
    return Results.Accepted($"/api/v1/sessions/{sessionId}/actions/{result.Id}", result);
}).RequireAuthorization(PlatformPolicies.Student);
app.MapPost("/api/v1/sessions/{sessionId:guid}/rounds/current/execute", async (Guid sessionId,
    ExecuteRoundRequest request, ClaimsPrincipal principal, HttpContext http, PlatformDbContext db, IAuthorizationService authorization,
    ExecuteRoundHandler handler, CancellationToken ct) =>
{
    var ownership = await db.Sessions.Where(x => x.Id == sessionId)
        .Join(db.Classrooms, session => session.ClassroomId, classroom => classroom.Id, (session, classroom) => classroom)
        .Join(db.Courses, classroom => classroom.CourseId, course => course.Id,
            (_, course) => new OwnedSessionResource(course.OwnerUserId)).SingleOrDefaultAsync(ct);
    if (ownership is null) return Results.NotFound();
    var allowed = await authorization.AuthorizeAsync(principal, ownership, new ManageOwnedResourceRequirement());
    if (!allowed.Succeeded) return Results.NotFound();
    var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
    if (!Guid.TryParse(subject, out var actorId)) return Results.Unauthorized();
    return Results.Ok(await handler.HandleAsync(new(sessionId, request.TeamId, actorId, request.ExecutionId, http.TraceIdentifier), ct));
}).RequireAuthorization(PlatformPolicies.Instructor);
app.MapHub<SessionHub>("/hubs/sessions");
app.MapClassroomWorkflow();
app.MapMacroGameplay();
app.MapCompetitiveMarket();
app.MapScenarioDiscovery();
app.MapAuthoring();
app.MapScenarioProposals();
app.Run();

public sealed record SubmitActionRequest(Guid TeamId, Guid RoleAssignmentId, string ActionCode, JsonElement Payload);
public sealed record RegisterRequest(string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ExecuteRoundRequest(Guid TeamId, Guid ExecutionId);
public sealed record OwnedSessionResource(Guid OwnerUserId) : IOwnedResource;
[Authorize]
public sealed class SessionHub(PlatformDbContext db) : Microsoft.AspNetCore.SignalR.Hub
{
    public async Task JoinSession(Guid sessionId)
    {
        var subject = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub");
        if (!Guid.TryParse(subject, out var userId)) throw new HubException("authentication.required");
        var participant = await db.Participants.AsNoTracking().SingleOrDefaultAsync(x => x.SessionId == sessionId && x.UserId == userId);
        var owns = participant is null && await db.Sessions.Where(x => x.Id == sessionId)
            .Join(db.Classrooms, x => x.ClassroomId, x => x.Id, (_, room) => room)
            .Join(db.Courses, x => x.CourseId, x => x.Id, (_, course) => course.OwnerUserId)
            .AnyAsync(x => x == userId);
        if (participant is null && !owns) throw new HubException("session.not_found");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        if (participant?.TeamId is Guid teamId)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}:team:{teamId}");
    }

    public Task LeaveSession(Guid sessionId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"session:{sessionId}");
}
public sealed class SignalRIntegrationEventPublisher(Microsoft.AspNetCore.SignalR.IHubContext<SessionHub> hub)
    : IIntegrationEventPublisher
{
    public async ValueTask PublishAsync(Guid messageId, string type, string payloadJson, CancellationToken ct)
    {
        using var document = JsonDocument.Parse(payloadJson);
        var root = document.RootElement;
        var hasId = root.TryGetProperty("sessionId", out var id) || root.TryGetProperty("SessionId", out id);
        var sessionId = hasId && id.ValueKind == JsonValueKind.String && id.TryGetGuid(out var parsed) ? parsed : Guid.Empty;
        if (sessionId == Guid.Empty) return;
        await hub.Clients.Group($"session:{sessionId}").SendAsync(type, new { messageId, payload = document.RootElement.Clone() }, ct);
    }
}
public partial class Program;
public static class AuditFactory
{
    public static AuditRow Create(Guid? actor, string action, string resourceType, string resourceId, string traceId, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), ActorUserId = actor, Action = action, ResourceType = resourceType,
            ResourceId = resourceId, TraceId = traceId, MetadataJson = "{}", OccurredAt = at };
}
