using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PosConnector : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PosProductRef",
                table: "MenuItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PosCheckRef",
                table: "Checks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PosCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CheckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Uncertain = table.Column<bool>(type: "bit", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResultRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosCommands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosCommands_Checks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosCommands_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosCommands_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PosConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BranchRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AutoClose = table.Column<bool>(type: "bit", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastCheckHealthy = table.Column<bool>(type: "bit", nullable: true),
                    LastCheckMessage = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PosConnections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosConnections_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosCommands_CheckId_Kind",
                table: "PosCommands",
                columns: new[] { "CheckId", "Kind" },
                unique: true,
                filter: "[Kind] = 'CloseCheck'");

            migrationBuilder.CreateIndex(
                name: "IX_PosCommands_Kind_OrderId",
                table: "PosCommands",
                columns: new[] { "Kind", "OrderId" },
                unique: true,
                filter: "[OrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PosCommands_Kind_PaymentId",
                table: "PosCommands",
                columns: new[] { "Kind", "PaymentId" },
                unique: true,
                filter: "[PaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PosCommands_Status_NextAttemptAt",
                table: "PosCommands",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PosCommands_TenantId",
                table: "PosCommands",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PosCommands_VenueId_CreatedAt",
                table: "PosCommands",
                columns: new[] { "VenueId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PosConnections_TenantId",
                table: "PosConnections",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PosConnections_VenueId",
                table: "PosConnections",
                column: "VenueId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosCommands");

            migrationBuilder.DropTable(
                name: "PosConnections");

            migrationBuilder.DropColumn(
                name: "PosProductRef",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "PosCheckRef",
                table: "Checks");
        }
    }
}
