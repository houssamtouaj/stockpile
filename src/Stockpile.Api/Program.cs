using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Stockpile.Api.Authorization;
using Stockpile.Api.Common;
using Stockpile.Api.Endpoints;
using Stockpile.Application;
using Stockpile.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<StockpileExceptionHandler>();

// Enums cross the wire as their names, not their ordinals. A client branching on
// "ReservationHold" keeps working when a new MovementType is inserted in the middle of
// the enum; a client branching on 6 silently starts reading the wrong thing.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var jwt = builder.Configuration.GetSection("Jwt");
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwt["SigningKey"]!)),

            // ClockSkew defaults to five minutes, which silently keeps expired tokens
            // working. Thirty seconds is the honest setting and makes the 15-minute
            // expiry mean what it says.
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddStockpileOpenApi();

builder.Services.AddAuthorizationBuilder().AddStockpilePolicies();

var app = builder.Build();

// First in the pipeline, so nothing downstream can escape as a bare 500. AddProblemDetails()
// above is only half of it: it supplies the document shape, this supplies the handler that
// produces one at all.
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

var openApi = app.MapOpenApi();

var scalar = app.MapScalarApiReference(options => options
    .WithTitle("Stockpile API")
    .WithTheme(ScalarTheme.BluePlanet));

// Anonymous locally and under test; behind a login on the deployed demo — a portfolio
// API nobody can browse is half a demo, but it should not be open to the internet.
// Test on Production, not on Development: the integration host runs as "Testing", so
// IsDevelopment() is false there and OpenApiTests would get a 401 from an anonymous
// client. Naming the environment that must be locked down is the honest polarity.
//
// BOTH are gated, not just the UI. /scalar/v1 only renders what /openapi/v1.json
// serves, so locking the renderer while leaving the document anonymous would publish
// the entire route table, every request schema and every auth requirement to anyone
// who guessed the second URL — and leave a gate that looks closed but is not.
if (app.Environment.IsProduction())
{
    openApi.RequireAuthorization(Policies.CanView);
    scalar.RequireAuthorization(Policies.CanView);
}

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapCatalogueEndpoints();
app.MapStockEndpoints();

app.Run();

public partial class Program;   // makes the entry point visible to WebApplicationFactory
