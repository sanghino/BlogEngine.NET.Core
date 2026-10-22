using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogEngine.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSiteAggregation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSiteAggregation",
                table: "Blogs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSiteAggregation",
                table: "Blogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
