using Azure.Monitor.OpenTelemetry.AspNetCore;
using Fulfillment.Api;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationHandler>();
builder.Services.AddFulfillmentDatabase(builder.Configuration);
builder.Services.AddServiceClients(builder.Configuration);
builder.Services.AddScoped<FulfillmentService>();
builder.Services.AddHealthChecks()
    .AddDbContextCheck<FulfillmentDbContext>("fulfillment-database");

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
app.UseDownstreamFailures();
if (builder.Configuration.GetValue("Database:Initialize", defaultValue: true))
{
    await FulfillmentBootstrap.InitializeAsync(app.Services);
}

app.MapPost(
    "/internal/fulfillments/{orderId:guid}",
    async (
        Guid orderId,
        FulfillmentService fulfillment,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var order = await fulfillment.FulfillAsync(orderId, cancellationToken);
            return order is null ? Results.NotFound() : Results.Ok(order);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(
                new ProblemDetails { Title = exception.Message, Status = 409 });
        }
    });

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
await app.RunAsync();

namespace Fulfillment.Api
{
    public partial class Program;
}
