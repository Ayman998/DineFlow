using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HelloZen.Infrastructure.Persistence.Migrations
{
    /// <summary>Orders served on bills that were already paid in full become Closed, as new ones do.</summary>
    public partial class ClosedOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE o SET o.[Status] = N'Closed'
                FROM [Orders] o
                JOIN [Checks] c ON c.[Id] = o.[CheckId]
                WHERE o.[Status] = N'Completed' AND c.[Status] = N'Closed';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [Orders] SET [Status] = N'Completed' WHERE [Status] = N'Closed';");
        }
    }
}
