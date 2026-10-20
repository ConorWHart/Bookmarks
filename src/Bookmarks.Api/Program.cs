using System.Reflection;
using Bookmarks.Api.Auth;
using Bookmarks.Api.Data;
using Bookmarks.Api.Endpoints;
using Bookmarks.Api.Sync;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Liveness only. Deliberately no database check: App Service pings /health every minute, which would keep
// the serverless database from ever auto-pausing (and use up the free vCore allowance).
builder.Services.AddHealthChecks();

builder.Services.AddSingleton(TimeProvider.System);

// Entra External ID bearer tokens or our own device sessions (bms_…); every /api query is scoped to the
// caller's user (see BookmarksDbContext).
builder.Services.AddBookmarksAuth(builder.Configuration);
builder.Services.AddBookmarksData();

// The WASM client is served from a different origin (Static Web Apps / localhost:7101).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// /api/sync/push and /api/sync/pull (contracts in Bookmarks.Shared.Sync).
builder.Services.AddScoped<SyncService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api")
    .RequireAuthorization(AuthConstants.ApiUserPolicy);

// Informational version: "1.0.0+<commit>" (the SDK appends the commit), shown in the app's Settings → About.
api.MapGet("/version", () => TypedResults.Ok(new VersionInfo(
        "Bookmarks.Api",
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(Program).Assembly.GetName().Version?.ToString()
            ?? "0.0.0")))
    .WithName("GetVersion")
    .AllowAnonymous();

api.MapMeEndpoints();
api.MapSessionEndpoints();
api.MapSyncEndpoints();

app.Run();

internal sealed record VersionInfo(string Name, string Version);

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
