using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitBill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Mode",
                table: "Payments",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Full"); // every payment before splitting existed paid the whole bill

            migrationBuilder.AddColumn<int>(
                name: "Shares",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SplitPeople",
                table: "Payments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Checks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SplitPeople",
                table: "Checks",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnitIndex = table.Column<int>(type: "int", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    GuestSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemClaims_Checks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemClaims_GuestSessions_GuestSessionId",
                        column: x => x.GuestSessionId,
                        principalTable: "GuestSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemClaims_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemClaims_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemClaims_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItemClaims_CheckId",
                table: "ItemClaims",
                column: "CheckId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemClaims_GuestSessionId",
                table: "ItemClaims",
                column: "GuestSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemClaims_OrderLineId_UnitIndex",
                table: "ItemClaims",
                columns: new[] { "OrderLineId", "UnitIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemClaims_PaymentId",
                table: "ItemClaims",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemClaims_TenantId",
                table: "ItemClaims",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItemClaims");

            migrationBuilder.DropColumn(
                name: "Mode",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Shares",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "SplitPeople",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Checks");

            migrationBuilder.DropColumn(
                name: "SplitPeople",
                table: "Checks");
        }
    }
}
