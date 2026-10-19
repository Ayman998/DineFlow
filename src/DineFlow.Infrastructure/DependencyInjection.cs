using DineFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DineFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Connection string 'Postgres' is missing.");
        var redis = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Connection string 'Redis' is missing.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(postgres).UseSnakeCaseNamingConvention());

        services.AddHealthChecks()
            .AddNpgSql(postgres, name: "postgres")
            .AddRedis(redis, name: "redis");

        return services;
    }
}
