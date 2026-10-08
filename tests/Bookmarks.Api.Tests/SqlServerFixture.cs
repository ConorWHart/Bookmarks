using System.Runtime.InteropServices;
using Bookmarks.Api.Auth;
using Bookmarks.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Bookmarks.Api.Tests;

/// <summary>
/// A throwaway SQL Server in Docker (Testcontainers), shared by the tests in a collection.
/// When Docker isn't available, <see cref="DockerFactAttribute"/> tests are skipped and this does nothing.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer? _container = DockerFactAttribute.DockerAvailable
        ? new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build()
        : null;

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (_container is null)
        {
            return;
        }

        await _container.StartAsync();

        ConnectionString = new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = "BookmarksTests",
        }.ConnectionString;

        // Creates the schema straight from the model. Migrations are exercised separately (CI script step).
        await using var db = CreateContext(null);
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>A context as seen by <paramref name="userId"/> (null = no user).</summary>
    public BookmarksDbContext CreateContext(Guid? userId)
    {
        var options = new DbContextOptionsBuilder<BookmarksDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        return new BookmarksDbContext(options, userId is { } id ? (ICurrentUser)new FixedUser(id) : NoCurrentUser.Instance);
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}

/// <summary>A [Fact] that is skipped when no Docker engine is reachable (e.g. Docker Desktop not running).</summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public static readonly bool DockerAvailable = DetectDocker();

    public DockerFactAttribute()
    {
        if (!DockerAvailable)
        {
            Skip = "Docker is not available (start Docker Desktop to run SQL Server tests).";
        }
    }

    private static bool DetectDocker()
    {
        try
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
            {
                return true;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return Directory.GetFiles(@"\\.\pipe\").Any(p =>
                    p.EndsWith("docker_engine", StringComparison.OrdinalIgnoreCase));
            }

            return File.Exists("/var/run/docker.sock");
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
