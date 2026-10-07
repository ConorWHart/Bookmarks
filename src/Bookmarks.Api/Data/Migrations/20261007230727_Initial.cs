using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarks.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bookcases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Theme = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    SortRank = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookcases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Isbn13 = table.Column<string>(type: "varchar(13)", unicode: false, maxLength: 13, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Subtitle = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Authors = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Publisher = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    PublishedYear = table.Column<int>(type: "int", nullable: true),
                    PageCount = table.Column<int>(type: "int", nullable: true),
                    Genres = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Series = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SeriesNumber = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: true),
                    CoverUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    HeightMm = table.Column<int>(type: "int", nullable: true),
                    WidthMm = table.Column<int>(type: "int", nullable: true),
                    ThicknessMm = table.Column<int>(type: "int", nullable: true),
                    DominantColor = table.Column<string>(type: "varchar(9)", unicode: false, maxLength: 9, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LibraryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Format = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Condition = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    ShelfId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ShelfRank = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcquiredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    PurchasePrice = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    LentTo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    WantToReread = table.Column<bool>(type: "bit", nullable: false),
                    Rating = table.Column<decimal>(type: "decimal(3,1)", precision: 3, scale: 1, nullable: true),
                    TagIds = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Appearance_FaceOut = table.Column<bool>(type: "bit", nullable: false),
                    Appearance_HeightMmOverride = table.Column<int>(type: "int", nullable: true),
                    Appearance_SpineColor = table.Column<string>(type: "varchar(9)", unicode: false, maxLength: 9, nullable: true),
                    Appearance_SpineImageUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    Appearance_Style = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    Appearance_TextColor = table.Column<string>(type: "varchar(9)", unicode: false, maxLength: 9, nullable: true),
                    Appearance_ThicknessMmOverride = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LibraryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Presets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false),
                    SchemaVersion = table.Column<int>(type: "int", nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Presets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReadingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LibraryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    FinishedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Rating = table.Column<decimal>(type: "decimal(3,1)", precision: 3, scale: 1, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shelves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookcaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortRank = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    WidthMm = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shelves", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "varchar(9)", unicode: false, maxLength: 9, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAccounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookcases_UserId_Version",
                table: "Bookcases",
                columns: new[] { "UserId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Books_CreatedByUserId",
                table: "Books",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Books_Isbn13",
                table: "Books",
                column: "Isbn13");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItems_BookId",
                table: "LibraryItems",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItems_ShelfId",
                table: "LibraryItems",
                column: "ShelfId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItems_UserId_Version",
                table: "LibraryItems",
                columns: new[] { "UserId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_LibraryItemId",
                table: "Notes",
                column: "LibraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Notes_UserId_Version",
                table: "Notes",
                columns: new[] { "UserId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Presets_UserId_Version",
                table: "Presets",
                columns: new[] { "UserId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_LibraryItemId",
                table: "ReadingSessions",
                column: "LibraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_UserId_Version",
                table: "ReadingSessions",
                columns: new[] { "UserId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Shelves_BookcaseId",
                table: "Shelves",
                column: "BookcaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Shelves_UserId_Version",
                table: "Shelves",
                columns: new[] { "UserId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_Tags_UserId_Version",
                table: "Tags",
                columns: new[] { "UserId", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bookcases");

            migrationBuilder.DropTable(
                name: "Books");

            migrationBuilder.DropTable(
                name: "LibraryItems");

            migrationBuilder.DropTable(
                name: "Notes");

            migrationBuilder.DropTable(
                name: "Presets");

            migrationBuilder.DropTable(
                name: "ReadingSessions");

            migrationBuilder.DropTable(
                name: "Shelves");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "UserAccounts");
        }
    }
}
