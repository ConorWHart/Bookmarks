var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// The WASM client is served from a different origin (Static Web Apps / localhost:7101).
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

// Phase 3: EF Core (Azure SQL) + Entra External ID JWT bearer auth.
// Phase 4: /api/sync/push and /api/sync/pull (contracts in Bookmarks.Shared.Sync).

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors();

app.MapHealthChecks("/health");

var api = app.MapGroup("/api");

api.MapGet("/version", () => TypedResults.Ok(new VersionInfo(
        "Bookmarks.Api",
        typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0")))
    .WithName("GetVersion");

app.Run();

internal sealed record VersionInfo(string Name, string Version);

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
