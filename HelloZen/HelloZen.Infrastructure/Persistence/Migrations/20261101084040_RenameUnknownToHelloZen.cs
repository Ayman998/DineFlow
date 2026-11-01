using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloZen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameUnknownToHelloZen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Discrepancy kinds are stored by name, and UnknownToDineFlow became UnknownToHelloZen with the rename.
            migrationBuilder.Sql("UPDATE [Discrepancies] SET [Kind] = N'UnknownToHelloZen' WHERE [Kind] = N'UnknownToDineFlow';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Discrepancies] SET [Kind] = N'UnknownToDineFlow' WHERE [Kind] = N'UnknownToHelloZen';");
        }
    }
}
