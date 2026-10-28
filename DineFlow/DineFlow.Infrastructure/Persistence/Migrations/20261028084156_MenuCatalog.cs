using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MenuCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dropped first: while it cascades, deleting a menu would reach the links two ways,
            // which SQL Server refuses.
            migrationBuilder.DropForeignKey(
                name: "FK_MenuCategories_Menus_MenuId",
                table: "MenuCategories");

            migrationBuilder.CreateTable(
                name: "MenuItemLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MenuId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MenuItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MenuItemLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MenuItemLinks_MenuItems_MenuItemId",
                        column: x => x.MenuItemId,
                        principalTable: "MenuItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuItemLinks_Menus_MenuId",
                        column: x => x.MenuId,
                        principalTable: "Menus",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MenuItemLinks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Categories now belong to the venue: every item stays on the menu its category was in.
            migrationBuilder.Sql("""
                INSERT INTO MenuItemLinks (Id, TenantId, MenuId, MenuItemId, CreatedAt)
                SELECT NEWID(), i.TenantId, c.MenuId, i.Id, SYSDATETIMEOFFSET()
                FROM MenuItems i
                JOIN MenuCategories c ON c.Id = i.CategoryId;
                """);

            // Sort orders were per menu; number the top-level categories across the venue in menu order.
            migrationBuilder.Sql("""
                UPDATE c SET SortOrder = r.NewOrder
                FROM MenuCategories c
                JOIN (
                    SELECT c2.Id, ROW_NUMBER() OVER (PARTITION BY c2.VenueId ORDER BY m.SortOrder, c2.SortOrder, c2.Name) - 1 AS NewOrder
                    FROM MenuCategories c2
                    JOIN Menus m ON m.Id = c2.MenuId
                    WHERE c2.ParentId IS NULL
                ) r ON r.Id = c.Id;
                """);

            migrationBuilder.DropIndex(
                name: "IX_MenuCategories_MenuId_SortOrder",
                table: "MenuCategories");

            migrationBuilder.DropColumn(
                name: "MenuId",
                table: "MenuCategories");

            migrationBuilder.CreateIndex(
                name: "IX_MenuCategories_VenueId_ParentId_SortOrder",
                table: "MenuCategories",
                columns: new[] { "VenueId", "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemLinks_MenuId_MenuItemId",
                table: "MenuItemLinks",
                columns: new[] { "MenuId", "MenuItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemLinks_MenuItemId",
                table: "MenuItemLinks",
                column: "MenuItemId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItemLinks_TenantId",
                table: "MenuItemLinks",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_MenuCategories_Venues_VenueId",
                table: "MenuCategories",
                column: "VenueId",
                principalTable: "Venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MenuCategories_Venues_VenueId",
                table: "MenuCategories");

            migrationBuilder.DropIndex(
                name: "IX_MenuCategories_VenueId_ParentId_SortOrder",
                table: "MenuCategories");

            migrationBuilder.AddColumn<Guid>(
                name: "MenuId",
                table: "MenuCategories",
                type: "uniqueidentifier",
                nullable: true);

            // Each top-level category goes back to a menu that shows one of its items, else the
            // venue's first menu; subcategories follow their parent. Categories of a venue with no
            // menus can't be kept.
            migrationBuilder.Sql("""
                UPDATE c SET MenuId = COALESCE(
                    (SELECT TOP 1 l.MenuId FROM MenuItemLinks l
                     JOIN MenuItems i ON i.Id = l.MenuItemId
                     JOIN MenuCategories ic ON ic.Id = i.CategoryId
                     WHERE ic.Id = c.Id OR ic.ParentId = c.Id),
                    (SELECT TOP 1 m.Id FROM Menus m WHERE m.VenueId = c.VenueId ORDER BY m.SortOrder))
                FROM MenuCategories c
                WHERE c.ParentId IS NULL;

                UPDATE s SET MenuId = p.MenuId
                FROM MenuCategories s
                JOIN MenuCategories p ON p.Id = s.ParentId;

                DELETE FROM MenuCategories WHERE MenuId IS NULL AND ParentId IS NOT NULL;
                DELETE FROM MenuCategories WHERE MenuId IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "MenuId",
                table: "MenuCategories",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            // Before the cascade from menus to categories comes back, for the same reason as in Up.
            migrationBuilder.DropTable(
                name: "MenuItemLinks");

            migrationBuilder.CreateIndex(
                name: "IX_MenuCategories_MenuId_SortOrder",
                table: "MenuCategories",
                columns: new[] { "MenuId", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_MenuCategories_Menus_MenuId",
                table: "MenuCategories",
                column: "MenuId",
                principalTable: "Menus",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
