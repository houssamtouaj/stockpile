using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Stockpile.Application.Common.Behaviors;
using Stockpile.Application.Common.Stock;

namespace Stockpile.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<AssemblyMarker>();

            // Order matters: validate before opening a transaction, log the whole thing.
            // Validation runs OUTSIDE the transaction — opening one, discovering the
            // request was malformed and rolling back is a wasted round trip on every bad
            // request, and under load that is a measurable share of them.
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
        });

        services.AddValidatorsFromAssemblyContaining<AssemblyMarker>(includeInternalTypes: true);

        services.AddScoped<IStockMutator, StockMutator>();

        return services;
    }
}
