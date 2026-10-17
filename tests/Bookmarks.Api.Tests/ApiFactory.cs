using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Bookmarks.Api.Tests;

/// <summary>
/// The real API pipeline with <see cref="TestAuthHandler"/> instead of JwtBearer (device sessions stay real).
/// Pass a connection string for tests that reach the database; without one, only auth/pipeline behavior is testable.
/// Pass a clock to control time (device session expiry).
/// </summary>
public sealed class ApiFactory(string? connectionString = null, TimeProvider? time = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Placeholder values: JwtBearer is registered but never used (the default scheme is overridden below).
        builder.UseSetting("Auth:Authority", "https://example.ciamlogin.com/00000000-0000-0000-0000-000000000000/v2.0");
        builder.UseSetting("Auth:Audience", "00000000-0000-0000-0000-000000000001");

        if (connectionString is not null)
        {
            builder.UseSetting("ConnectionStrings:Bookmarks", connectionString);
        }

        builder.ConfigureTestServices(services =>
        {
            // The real selector scheme still sends bms_… tokens to device sessions; everything else goes to the test handler.
            services
                .AddAuthentication()
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.Configure<Bookmarks.Api.Auth.BookmarksAuthSchemes>(o => o.TokenScheme = TestAuthHandler.SchemeName);

            if (time is not null)
            {
                services.AddSingleton(time);
            }
        });
    }

    public HttpClient CreateClientFor(Guid? userId, string? scopes = null, string? name = null)
    {
        var client = CreateClient();
        if (userId is { } id)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, id.ToString());
        }

        if (scopes is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.ScopesHeader, scopes);
        }

        if (name is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.NameHeader, name);
        }

        return client;
    }
}
