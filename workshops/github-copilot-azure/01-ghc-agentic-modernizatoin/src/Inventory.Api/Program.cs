using Azure.Monitor.OpenTelemetry.AspNetCore;
using Inventory.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddInventoryDatabase(builder.Configuration);
builder.Services.AddScoped<InventoryService>();
builder.Services.AddHealthChecks().AddDbContextCheck<InventoryDbContext>("inventory-database");

var applicationInsightsConnectionString =
    builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
{
    builder.Services
        .AddOpenTelemetry()
        .UseAzureMonitor(options => options.ConnectionString = applicationInsightsConnectionString);
}

var app = builder.Build();
app.UseCorrelation();
if (builder.Configuration.GetValue("Database:Initialize", defaultValue: true))
{
    await InventoryBootstrap.InitializeAsync(app.Services);
}

app.MapGet(
    "/internal/inventory/{sku}",
    async (string sku, InventoryService inventory, CancellationToken cancellationToken) =>
    {
        var item = await inventory.GetAsync(sku, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    });

app.MapGet(
    "/internal/inventory/{sku}/availability",
    async (
        string sku,
        int quantity,
        InventoryService inventory,
        CancellationToken cancellationToken) =>
            Results.Ok(
                new AvailabilityResponse(
                    await inventory.CanReserveAsync(sku, quantity, cancellationToken))));

app.MapPost(
    "/internal/inventory/{sku}/reservations",
    async (
        string sku,
        ReservationRequest request,
        InventoryService inventory,
        CancellationToken cancellationToken) =>
    {
        try
        {
            return Results.Ok(
                await inventory.ReserveAsync(
                    sku,
                    request.ReservationId,
                    request.Quantity,
                    cancellationToken));
        }
        catch (InventoryConflictException exception)
        {
            return Results.Conflict(
                new ProblemDetails { Title = exception.Message, Status = 409 });
        }
    });

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
await app.RunAsync();

namespace Inventory.Api
{
    public partial class Program;
}
