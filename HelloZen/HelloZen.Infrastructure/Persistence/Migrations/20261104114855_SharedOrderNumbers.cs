using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloZen.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// A table's orders share its bill's number as rounds 1, 2, 3…; bills hold the numbers, unique per
    /// venue. Existing orders become round 1, and each existing bill takes its first order's number.
    /// </summary>
    public partial class SharedOrderNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_VenueId_Number",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Checks_VenueId",
                table: "Checks");

            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "Checks",
                type: "int",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE c SET c.[Number] = (SELECT MIN(o.[Number]) FROM [Orders] o WHERE o.[CheckId] = c.[Id])
                FROM [Checks] c;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_VenueId_Number",
                table: "Orders",
                columns: new[] { "VenueId", "Number" });

            migrationBuilder.CreateIndex(
                name: "IX_Checks_VenueId_Number",
                table: "Checks",
                columns: new[] { "VenueId", "Number" },
                unique: true,
                filter: "[Number] IS NOT NULL");
        }

        /// <inheritdoc />
        /// <remarks>Fails once a table has ordered a second round, since its rounds share a number.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_VenueId_Number",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Checks_VenueId_Number",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "Checks");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_VenueId_Number",
                table: "Orders",
                columns: new[] { "VenueId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Checks_VenueId",
                table: "Checks",
                column: "VenueId");
        }
    }
}
