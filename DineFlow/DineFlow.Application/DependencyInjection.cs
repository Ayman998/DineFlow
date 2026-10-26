using DineFlow.Application.Audit;
using DineFlow.Application.Auth;
using DineFlow.Application.Notifications;
using DineFlow.Application.Reports;
using DineFlow.Application.Bills;
using DineFlow.Application.Dashboard;
using DineFlow.Application.Finance;
using DineFlow.Application.Guests;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.PaymentLinks;
using DineFlow.Application.Pos;
using DineFlow.Application.Payments;
using DineFlow.Application.Promotions;
using DineFlow.Application.Reviews;
using DineFlow.Application.Tables;
using DineFlow.Application.Transactions;
using DineFlow.Application.Venues;
using Microsoft.Extensions.DependencyInjection;

namespace DineFlow.Application;

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
        return services;
    }
}
