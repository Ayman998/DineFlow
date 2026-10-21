using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderLinePosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "OrderLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Number the lines of existing orders in the order they were created.
            migrationBuilder.Sql(
                "WITH numbered AS (SELECT Id, ROW_NUMBER() OVER (PARTITION BY OrderId ORDER BY CreatedAt, Id) - 1 AS Position FROM OrderLines) " +
                "UPDATE l SET Position = n.Position FROM OrderLines l JOIN numbered n ON n.Id = l.Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Position",
                table: "OrderLines");
        }
    }
}
