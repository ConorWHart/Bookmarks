using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarks.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6bPlacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Bookcases",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ShelfPlacements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShelfId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LibraryItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Rank = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShelfPlacements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShelfPlacements_LibraryItemId",
                table: "ShelfPlacements",
                column: "LibraryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ShelfPlacements_ShelfId",
                table: "ShelfPlacements",
                column: "ShelfId");

            migrationBuilder.CreateIndex(
                name: "IX_ShelfPlacements_UserId_Version",
                table: "ShelfPlacements",
                columns: new[] { "UserId", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShelfPlacements");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Bookcases");
        }
    }
}
