using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BlogEngine.Data.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class PageShowInMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowInMenu",
                table: "Pages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Prima dell'opzione separata il menu seguiva ShowInList: le pagine esistenti restano com'erano.
            migrationBuilder.Sql("UPDATE Pages SET ShowInMenu = ShowInList;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShowInMenu",
                table: "Pages");
        }
    }
}
