using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tests.Integration;

public sealed class AuthorizationEndpointTests : IClassFixture<SecureApiFactory>
{
    private readonly HttpClient _client;
    public AuthorizationEndpointTests(SecureApiFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Anonymous_action_submission_is_unauthorized()
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/sessions/{Guid.NewGuid()}/actions", new
        {
            teamId = Guid.NewGuid(), roleAssignmentId = Guid.NewGuid(), actionCode = "ANY", payload = new { },
            userId = Guid.NewGuid()
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Instructor_platform_role_cannot_use_student_action_endpoint()
    {
        _client.DefaultRequestHeaders.Add("X-Test-Role", "Instructor");
        var response = await _client.PostAsJsonAsync($"/api/v1/sessions/{Guid.NewGuid()}/actions", new
        { teamId = Guid.NewGuid(), roleAssignmentId = Guid.NewGuid(), actionCode = "ANY", payload = new { } });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

public sealed class SecureApiFactory : WebApplicationFactory<Program>
{
    internal const string Key = "integration-test-key-that-is-at-least-32-bytes-long";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Platform", "Host=unused;Database=unused;Username=unused;Password=unused");
        builder.UseSetting("Authentication:Jwt:Issuer", "SimulationPlatform");
        builder.UseSetting("Authentication:Jwt:Audience", "SimulationPlatform.Client");
        builder.UseSetting("Authentication:Jwt:SigningKey", Key);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> {
                ["ConnectionStrings:Platform"] = "Host=unused;Database=unused;Username=unused;Password=unused",
                ["Authentication:Jwt:Issuer"] = "SimulationPlatform",
                ["Authentication:Jwt:Audience"] = "SimulationPlatform.Client",
                ["Authentication:Jwt:SigningKey"] = Key }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultForbidScheme = "Test";
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
        });
    }
}

public sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Role, role.ToString())], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}
