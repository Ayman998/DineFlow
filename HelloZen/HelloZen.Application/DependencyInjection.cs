using HelloZen.Application.Audit;
using HelloZen.Application.Auth;
using HelloZen.Application.Notifications;
using HelloZen.Application.Reports;
using HelloZen.Application.Bills;
using HelloZen.Application.Dashboard;
using HelloZen.Application.Finance;
using HelloZen.Application.Onboarding;
using HelloZen.Application.Staff;
using HelloZen.Application.Guests;
using HelloZen.Application.Menus;
using HelloZen.Application.Orders;
using HelloZen.Application.PaymentLinks;
using HelloZen.Application.Pos;
using HelloZen.Application.Payments;
using HelloZen.Application.Promotions;
using HelloZen.Application.Reviews;
using HelloZen.Application.Tables;
using HelloZen.Application.Transactions;
using HelloZen.Application.Venues;
using Microsoft.Extensions.DependencyInjection;

namespace HelloZen.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GuestService>();
        services.AddScoped<VenueService>();
        services.AddScoped<TableService>();
        services.AddScoped<AuthService>();
        services.AddScoped<GuestMenuService>();
        services.AddScoped<MenuAdminService>();
        services.AddScoped<MenuImageService>();
        services.AddScoped<CartQuoteService>();
        services.AddScoped<GuestOrderService>();
        services.AddScoped<MerchantOrderService>();
        services.AddScoped<BillService>();
        services.AddScoped<TableTransferService>();
        services.AddScoped<PromotionService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<PaymentStatusSync>();
        services.AddScoped<GuestReviewService>();
        services.AddScoped<MerchantReviewService>();
        services.AddScoped<TransactionService>();
        services.AddScoped<TableStatusService>();
        services.AddScoped<OverviewService>();
        services.AddScoped<PaymentLinkService>();
        services.AddScoped<PaymentLinkCheckoutService>();
        services.AddScoped<PaymentAlerts>();
        services.AddScoped<AlertService>();
        services.AddScoped<NotificationDispatcher>();
        services.AddScoped<ReportBuilder>();
        services.AddScoped<ReportService>();
        services.AddScoped<AuditService>();
        services.AddScoped<PosSync>();
        services.AddScoped<PosDispatcher>();
        services.AddScoped<PosService>();
        services.AddScoped<Reconciler>();
        services.AddScoped<FinanceService>();
        services.AddScoped<TeamService>();
        services.AddScoped<ProvisioningService>();
        services.AddScoped<GoLiveService>();
        return services;
    }
}
