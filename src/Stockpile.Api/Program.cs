using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Stockpile.Api.Authorization;
using Stockpile.Api.Endpoints;
using Stockpile.Application;
using Stockpile.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();

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

builder.Services.AddAuthorizationBuilder().AddStockpilePolicies();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapCatalogueEndpoints();
app.MapStockEndpoints();

app.Run();

public partial class Program;   // makes the entry point visible to WebApplicationFactory
