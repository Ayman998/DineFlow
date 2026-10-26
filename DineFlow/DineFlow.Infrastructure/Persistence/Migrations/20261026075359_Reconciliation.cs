using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Reconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Discrepancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SettlementLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ExpectedMinor = table.Column<long>(type: "bigint", nullable: false),
                    ActualMinor = table.Column<long>(type: "bigint", nullable: false),
                    Details = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByStaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Discrepancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Discrepancies_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Discrepancies_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    RanAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Payments = table.Column<int>(type: "int", nullable: false),
                    LedgerGrossMinor = table.Column<long>(type: "bigint", nullable: false),
                    LedgerTipsMinor = table.Column<long>(type: "bigint", nullable: false),
                    LedgerRefundsMinor = table.Column<long>(type: "bigint", nullable: false),
                    Settled = table.Column<int>(type: "int", nullable: false),
                    SettledGrossMinor = table.Column<long>(type: "bigint", nullable: false),
                    SettledFeesMinor = table.Column<long>(type: "bigint", nullable: false),
                    AwaitingSettlement = table.Column<int>(type: "int", nullable: false),
                    PostedToPos = table.Column<int>(type: "int", nullable: false),
                    Matched = table.Column<int>(type: "int", nullable: false),
                    OpenDiscrepancies = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReconciliationRuns_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Settlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    BatchRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    PayoutDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ImportedByStaffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GrossMinor = table.Column<long>(type: "bigint", nullable: false),
                    RefundsMinor = table.Column<long>(type: "bigint", nullable: false),
                    FeesMinor = table.Column<long>(type: "bigint", nullable: false),
                    NetMinor = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Settlements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Settlements_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettlementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SettlementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ProviderRefundId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    FeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettlementLines_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SettlementLines_Settlements_SettlementId",
                        column: x => x.SettlementId,
                        principalTable: "Settlements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SettlementLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancies_TenantId",
                table: "Discrepancies",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancies_VenueId_BusinessDate_Key",
                table: "Discrepancies",
                columns: new[] { "VenueId", "BusinessDate", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Discrepancies_VenueId_Status",
                table: "Discrepancies",
                columns: new[] { "VenueId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRuns_TenantId",
                table: "ReconciliationRuns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRuns_VenueId_BusinessDate",
                table: "ReconciliationRuns",
                columns: new[] { "VenueId", "BusinessDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_PaymentId",
                table: "SettlementLines",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_ProviderPaymentId",
                table: "SettlementLines",
                column: "ProviderPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_ProviderRefundId",
                table: "SettlementLines",
                column: "ProviderRefundId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_Reference",
                table: "SettlementLines",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_SettlementId",
                table: "SettlementLines",
                column: "SettlementId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_TenantId",
                table: "SettlementLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_TransactionDate",
                table: "SettlementLines",
                column: "TransactionDate");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_TenantId",
                table: "Settlements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_VenueId_PayoutDate",
                table: "Settlements",
                columns: new[] { "VenueId", "PayoutDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Settlements_VenueId_Provider_BatchRef",
                table: "Settlements",
                columns: new[] { "VenueId", "Provider", "BatchRef" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Discrepancies");

            migrationBuilder.DropTable(
                name: "ReconciliationRuns");

            migrationBuilder.DropTable(
                name: "SettlementLines");

            migrationBuilder.DropTable(
                name: "Settlements");
        }
    }
}
