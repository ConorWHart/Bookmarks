using Microsoft.EntityFrameworkCore;

namespace Bookmarks.Api.Data;

public static class DataSetup
{
    public const string ConnectionStringName = "Bookmarks";

    /// <summary>Local development database (SQL Server LocalDB, installed with Visual Studio).</summary>
    public const string LocalDbConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=Bookmarks;Trusted_Connection=True;TrustServerCertificate=True";

    public static IServiceCollection AddBookmarksData(this IServiceCollection services)
    {
        // The connection string is read when the context is created (not at startup), so tests can
        // override it with UseSetting and the app can start before a database is configured.
        services.AddDbContext<BookmarksDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

            // Azure SQL serverless auto-pauses: the first connection after a pause can take up to a minute
            // and may fail transiently while the database resumes.
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(
                maxRetryCount: 6,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null));
        });

        return services;
    }
}
