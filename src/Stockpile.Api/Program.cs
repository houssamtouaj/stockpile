using Stockpile.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;   // makes the entry point visible to WebApplicationFactory
