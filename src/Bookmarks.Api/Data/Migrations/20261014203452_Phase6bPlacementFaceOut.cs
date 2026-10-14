using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bookmarks.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6bPlacementFaceOut : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FaceOut",
                table: "ShelfPlacements",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaceOut",
                table: "ShelfPlacements");
        }
    }
}
