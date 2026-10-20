using DineFlow.Application.Auth;
using DineFlow.Application.Guests;
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
        return services;
    }
}
