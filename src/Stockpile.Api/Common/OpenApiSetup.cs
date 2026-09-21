using Microsoft.OpenApi;

namespace Stockpile.Api.Common;

public static class OpenApiSetup
{
    public static IServiceCollection AddStockpileOpenApi(this IServiceCollection services) =>
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Stockpile API",
                    Version = "v1",
                    Description =
                        "Multi-warehouse inventory and order management.\n\n"
                        + "Stock quantities are derived from an append-only movement ledger. "
                        + "Every stock-mutating endpoint requires an idempotency key. "
                        + "422 means a domain rule refused the request (including insufficient "
                        + "stock) and is an expected outcome of a well-formed call; 409 is "
                        + "reserved for optimistic-concurrency conflicts on edit-style "
                        + "aggregates and carries the current version."
                };

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??=
                    new Dictionary<string, IOpenApiSecurityScheme>();

                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Paste the accessToken returned by POST /api/auth/login."
                };

                return Task.CompletedTask;
            });
        });
}
