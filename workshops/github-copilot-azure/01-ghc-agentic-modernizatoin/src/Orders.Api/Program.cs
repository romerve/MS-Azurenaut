using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Mvc;
using Orders.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationHandler>();
builder.Services.AddOrdersDatabase(builder.Configuration);
builder.Services.AddScoped<OrderService>();
builder.Services.AddInventoryClient(builder.Configuration);
builder.Services.AddHealthChecks().AddDbContextCheck<OrdersDbContext>("orders-database");

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
    await OrdersBootstrap.InitializeAsync(app.Services);
}

app.MapGet(
    "/internal/orders",
    (OrderService orders, CancellationToken cancellationToken) =>
        orders.GetAllAsync(cancellationToken));

app.MapGet(
    "/internal/orders/{id:guid}",
    async (Guid id, OrderService orders, CancellationToken cancellationToken) =>
    {
        var order = await orders.GetAsync(id, cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(order);
    });

app.MapPost(
    "/internal/orders",
    async (
        CreateOrderRequest request,
        OrderService orders,
        CancellationToken cancellationToken) =>
    {
        try
        {
            var order = await orders.CreateAsync(request, cancellationToken);
            return Results.Created($"/internal/orders/{order.Id}", order);
        }
        catch (InvalidOperationException exception)
        {
            return Results.Conflict(
                new ProblemDetails { Title = exception.Message, Status = 409 });
        }
    });

app.MapPost(
    "/internal/orders/{id:guid}/fulfilled",
    async (Guid id, OrderService orders, CancellationToken cancellationToken) =>
    {
        var order = await orders.MarkFulfilledAsync(id, cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(order);
    });

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
await app.RunAsync();

namespace Orders.Api
{
    public partial class Program;
}
