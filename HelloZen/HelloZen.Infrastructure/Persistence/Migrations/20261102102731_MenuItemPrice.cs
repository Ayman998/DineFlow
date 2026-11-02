using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloZen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MenuItemPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PriceMinor",
                table: "MenuItemLinks",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceMinor",
                table: "MenuItemLinks");
        }
    }
}
