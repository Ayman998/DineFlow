using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DineFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MostCommonTipBasisPoints",
                table: "Venues",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipPresetsBasisPoints",
                table: "Venues",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<bool>(
                name: "TipsEnabled",
                table: "Venues",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Venues that existed before tipping get the same defaults new venues get.
            migrationBuilder.Sql("UPDATE [Venues] SET [TipsEnabled] = 1, [TipPresetsBasisPoints] = N'[500,1000,1500]', [MostCommonTipBasisPoints] = 1000;");

            migrationBuilder.AddColumn<Guid>(
                name: "CheckId",
                table: "Orders",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Checks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Checks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Checks_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Checks_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Checks_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedWebhooks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedWebhooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
                    MaxDiscountMinor = table.Column<long>(type: "bigint", nullable: true),
                    MinBillMinor = table.Column<long>(type: "bigint", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    MaxRedemptions = table.Column<int>(type: "int", nullable: true),
                    RedemptionCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ShowAfterPayment = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromoCodes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GuestSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderPaymentId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CheckoutUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    BillAmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    DiscountMinor = table.Column<long>(type: "bigint", nullable: false),
                    TipMinor = table.Column<long>(type: "bigint", nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    PromoCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PromoCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TermsVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CardBrand = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CardLast4 = table.Column<string>(type: "nchar(4)", fixedLength: true, maxLength: 4, nullable: true),
                    FailureReason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SucceededAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Checks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_GuestSessions_GuestSessionId",
                        column: x => x.GuestSessionId,
                        principalTable: "GuestSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_PromoCodes_PromoCodeId",
                        column: x => x.PromoCodeId,
                        principalTable: "PromoCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VenueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AmountMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "nchar(3)", fixedLength: true, maxLength: 3, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Checks_CheckId",
                        column: x => x.CheckId,
                        principalTable: "Checks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CheckId",
                table: "Orders",
                column: "CheckId");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_ServiceLocationId",
                table: "Checks",
                column: "ServiceLocationId",
                unique: true,
                filter: "[Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_TenantId",
                table: "Checks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Checks_VenueId",
                table: "Checks",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CheckId",
                table: "LedgerEntries",
                column: "CheckId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PaymentId",
                table: "LedgerEntries",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_TenantId",
                table: "LedgerEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_VenueId_CreatedAt",
                table: "LedgerEntries",
                columns: new[] { "VenueId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_CheckId_Status",
                table: "Payments",
                columns: new[] { "CheckId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GuestSessionId_ClientRequestId",
                table: "Payments",
                columns: new[] { "GuestSessionId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PromoCodeId",
                table: "Payments",
                column: "PromoCodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Provider_ProviderPaymentId",
                table: "Payments",
                columns: new[] { "Provider", "ProviderPaymentId" },
                unique: true,
                filter: "[ProviderPaymentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Reference",
                table: "Payments",
                column: "Reference");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId",
                table: "Payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_VenueId",
                table: "Payments",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWebhooks_Provider_EventId",
                table: "ProcessedWebhooks",
                columns: new[] { "Provider", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromoCodes_TenantId_Code",
                table: "PromoCodes",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Checks_CheckId",
                table: "Orders",
                column: "CheckId",
                principalTable: "Checks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Checks_CheckId",
                table: "Orders");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "ProcessedWebhooks");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Checks");

            migrationBuilder.DropTable(
                name: "PromoCodes");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CheckId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "MostCommonTipBasisPoints",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "TipPresetsBasisPoints",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "TipsEnabled",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "CheckId",
                table: "Orders");
        }
    }
}
