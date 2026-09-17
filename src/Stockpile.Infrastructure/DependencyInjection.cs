using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Infrastructure.Persistence;
using Stockpile.Infrastructure.Realtime;
using Stockpile.Infrastructure.Time;

namespace Stockpile.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<AppDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<INotificationPublisher, NotificationQueue>();
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}
