using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogEngine.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class PageSubpagesOptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubpagesColumns",
                table: "Pages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "SubpagesImages",
                table: "Pages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "SubpagesOrder",
                table: "Pages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubpagesPosition",
                table: "Pages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubpagesStyle",
                table: "Pages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SubpagesTitle",
                table: "Pages",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubpagesColumns",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SubpagesImages",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SubpagesOrder",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SubpagesPosition",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SubpagesStyle",
                table: "Pages");

            migrationBuilder.DropColumn(
                name: "SubpagesTitle",
                table: "Pages");
        }
    }
}
