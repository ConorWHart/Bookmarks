using Bookmarks.Api.Auth;
using Bookmarks.Shared.Domain;
using Bookmarks.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bookmarks.Api.Data;

/// <summary>
/// Azure SQL model. One instance per request; every user-owned query is scoped to the token's user
/// by the <see cref="OwnerFilter"/> query filter, so an endpoint can't forget the <c>WHERE UserId = …</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>No foreign-key constraints between synced tables: rows are created offline with client ids and can
/// arrive in any order (a note before its item, a tombstone before its parent). Relationships are indexed only.</item>
/// <item>Enums are stored as strings (same rule as the client's JSON), so reordering an enum never changes data.</item>
/// <item><see cref="SyncEntity.Version"/> is a SQL <c>rowversion</c>: database-wide, increases on every write.
/// <c>(UserId, Version)</c> is indexed for the Phase 4 pull (<c>WHERE UserId = @u AND Version &gt; @checkpoint</c>).</item>
/// </list>
/// </remarks>
public sealed class BookmarksDbContext(DbContextOptions<BookmarksDbContext> options, ICurrentUser currentUser)
    : DbContext(options)
{
    /// <summary>Hides other users' rows. Ignore only in admin/maintenance code.</summary>
    public const string OwnerFilter = "Owner";

    /// <summary>Hides tombstones. Phase 4's pull ignores it so deletes reach other devices.</summary>
    public const string NotDeletedFilter = "NotDeleted";

    /// <summary>Referenced by the query filters; EF parameterizes it per context instance. Null = no user = no rows.</summary>
    public Guid? CurrentUserId { get; } = currentUser.Id;

    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<LibraryItem> LibraryItems => Set<LibraryItem>();

    public DbSet<ReadingSession> ReadingSessions => Set<ReadingSession>();

    public DbSet<Note> Notes => Set<Note>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Bookcase> Bookcases => Set<Bookcase>();

    public DbSet<Shelf> Shelves => Set<Shelf>();

    public DbSet<Preset> Presets => Set<Preset>();

    // Short, ASCII-only columns (hex colors, rank keys, ISBNs).
    private const int ColorLength = 9;      // #RRGGBBAA
    private const int RankLength = 64;
    private const int UrlLength = 2048;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Applies to the nullable forms (e.g. BookCondition?) as well.
        configurationBuilder.Properties<OwnershipStatus>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<BookFormat>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<BookCondition>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<ReadingOutcome>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<BookSource>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<SpineStyle>().HaveConversion<string>().HaveMaxLength(24);
        configurationBuilder.Properties<PresetKind>().HaveConversion<string>().HaveMaxLength(24);

        configurationBuilder.Properties<decimal>().HavePrecision(10, 2);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("UserAccounts");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<Book>(e =>
        {
            e.ToTable("Books");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Isbn13).HasMaxLength(13).IsUnicode(false);
            e.Property(x => x.Title).HasMaxLength(500).IsRequired();
            e.Property(x => x.Subtitle).HasMaxLength(500);
            e.Property(x => x.Publisher).HasMaxLength(300);
            e.Property(x => x.Series).HasMaxLength(300);
            e.Property(x => x.SeriesNumber).HasPrecision(7, 2);
            e.Property(x => x.CoverUrl).HasMaxLength(UrlLength);
            e.Property(x => x.DominantColor).HasMaxLength(ColorLength).IsUnicode(false);

            // Authors and Genres are primitive collections (JSON in an nvarchar(max) column).

            // Not unique yet: each device creates catalog Book rows with its own ids today.
            // How catalog books are shared/deduplicated is decided in Phase 4/5.
            e.HasIndex(x => x.Isbn13);
            e.HasIndex(x => x.CreatedByUserId);
        });

        ConfigureSyncEntity<LibraryItem>(modelBuilder, "LibraryItems", e =>
        {
            e.Property(x => x.ShelfRank).HasMaxLength(RankLength).IsUnicode(false);
            e.Property(x => x.LentTo).HasMaxLength(200);
            e.Property(x => x.Rating).HasPrecision(3, 1);

            // TagIds is a primitive collection (JSON array of GUIDs).

            e.ComplexProperty(x => x.Appearance, a =>
            {
                a.Property(p => p.SpineColor).HasMaxLength(ColorLength).IsUnicode(false);
                a.Property(p => p.TextColor).HasMaxLength(ColorLength).IsUnicode(false);
                a.Property(p => p.SpineImageUrl).HasMaxLength(UrlLength);
            });

            e.HasIndex(x => x.BookId);
            e.HasIndex(x => x.ShelfId);
        });

        ConfigureSyncEntity<ReadingSession>(modelBuilder, "ReadingSessions", e =>
        {
            e.Property(x => x.Rating).HasPrecision(3, 1);
            e.HasIndex(x => x.LibraryItemId);
        });

        ConfigureSyncEntity<Note>(modelBuilder, "Notes", e =>
        {
            e.Property(x => x.Body).IsRequired(); // nvarchar(max)
            e.HasIndex(x => x.LibraryItemId);
        });

        ConfigureSyncEntity<Tag>(modelBuilder, "Tags", e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Color).HasMaxLength(ColorLength).IsUnicode(false);
        });

        ConfigureSyncEntity<Bookcase>(modelBuilder, "Bookcases", e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Theme).HasMaxLength(32).IsUnicode(false).IsRequired();
            e.Property(x => x.SortRank).HasMaxLength(RankLength).IsUnicode(false);
        });

        ConfigureSyncEntity<Shelf>(modelBuilder, "Shelves", e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.SortRank).HasMaxLength(RankLength).IsUnicode(false);
            e.HasIndex(x => x.BookcaseId);
        });

        ConfigureSyncEntity<Preset>(modelBuilder, "Presets", e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Json).IsRequired(); // opaque to the server, nvarchar(max)
        });
    }

    /// <summary>Shared mapping for every <see cref="SyncEntity"/>: key, rowversion, indexes and both query filters.</summary>
    private void ConfigureSyncEntity<T>(
        ModelBuilder modelBuilder,
        string table,
        Action<Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T>> configure)
        where T : SyncEntity
    {
        modelBuilder.Entity<T>(e =>
        {
            e.ToTable(table);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever(); // UUIDv7 from the client

            // rowversion (8 bytes, big-endian) <-> long. Server-assigned on every insert/update.
            e.Property(x => x.Version).IsRowVersion().HasConversion<byte[]>();

            e.HasIndex(x => new { x.UserId, x.Version });

            e.HasQueryFilter(OwnerFilter, x => x.UserId == CurrentUserId);
            e.HasQueryFilter(NotDeletedFilter, x => !x.IsDeleted);

            configure(e);
        });
    }
}
