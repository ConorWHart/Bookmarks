using Bookmarks.Api.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bookmarks.Api.Data;

/// <summary>
/// Used by <c>Add-Migration</c> / <c>dotnet ef</c> so the tools don't have to boot the web host
/// (no auth configuration needed, and CI can script migrations without a database connection).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BookmarksDbContext>
{
    public BookmarksDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BookmarksDbContext>()
            .UseSqlServer(DataSetup.LocalDbConnectionString)
            .Options;

        return new BookmarksDbContext(options, NoCurrentUser.Instance);
    }
}
