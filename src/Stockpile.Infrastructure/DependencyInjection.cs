using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stockpile.Application.Common.Interfaces;
using Stockpile.Infrastructure.Identity;
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
        services.AddScoped<IStockWriter, StockWriter>();
        services.AddScoped<IReconciliationReader, ReconciliationReader>();
        services.AddScoped<INotificationPublisher, NotificationQueue>();
        services.AddSingleton<IClock, SystemClock>();

        // AddIdentityCore rather than AddIdentity: AddIdentity installs cookie
        // authentication schemes and would fight the JWT scheme registered in Program.cs.
        // This API is token-only.
        services.AddIdentityCore<StockpileIdentityUser>(options =>
            {
                options.Password.RequiredLength = 12;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<AuthenticationService>();

        return services;
    }
}
