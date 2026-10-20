using DineFlow.Application.Auth;
using DineFlow.Application.Bills;
using DineFlow.Application.Guests;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.Payments;
using DineFlow.Application.Promotions;
using DineFlow.Application.Tables;
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
        return services;
    }
}
