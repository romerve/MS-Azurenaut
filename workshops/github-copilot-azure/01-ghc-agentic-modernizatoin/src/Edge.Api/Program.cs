using Azure.Monitor.OpenTelemetry.AspNetCore;
using Edge.Api;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<CorrelationHandler>();
builder.Services.AddServiceClients(builder.Configuration);
builder.Services.AddHealthChecks();

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

app.MapGet(
    "/api/orders",
    async (IOrdersClient orders, CancellationToken cancellationToken) =>
        Results.Ok(await orders.GetAllAsync(cancellationToken)));

app.MapGet(
    "/api/orders/{id:guid}",
    async (Guid id, IOrdersClient orders, CancellationToken cancellationToken) =>
    {
        var order = await orders.GetAsync(id, cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(order);
    });

app.MapPost(
    "/api/orders",
    async (
        CreateOrderRequest request,
        IOrdersClient orders,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId)
            || string.IsNullOrWhiteSpace(request.Sku)
            || request.Quantity <= 0)
        {
            return PublicApiResults.ValidationProblem();
        }

        var result = await orders.CreateAsync(request, cancellationToken);
        return result.Status switch
        {
            DownstreamStatus.Success => (IResult)Results.Created(
                $"/api/orders/{result.Value!.Id}",
                result.Value),
            DownstreamStatus.Conflict => PublicApiResults.Conflict("Insufficient inventory."),
            _ => throw new InvalidOperationException("Unexpected Orders response.")
        };
    });

app.MapPost(
    "/api/orders/{id:guid}/fulfill",
    async (
        Guid id,
        IFulfillmentClient fulfillment,
        CancellationToken cancellationToken) =>
    {
        var result = await fulfillment.FulfillAsync(id, cancellationToken);
        return result.Status switch
        {
            DownstreamStatus.Success => (IResult)Results.Ok(result.Value),
            DownstreamStatus.NotFound => Results.NotFound(),
            DownstreamStatus.Conflict => PublicApiResults.Conflict(
                result.Error ?? "Insufficient inventory."),
            _ => throw new InvalidOperationException("Unexpected Fulfillment response.")
        };
    });

app.MapGet(
    "/api/inventory/{sku}",
    async (string sku, IInventoryClient inventory, CancellationToken cancellationToken) =>
    {
        var item = await inventory.GetAsync(sku, cancellationToken);
        return item is null ? Results.NotFound() : Results.Ok(item);
    });

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
await app.RunAsync();

namespace Edge.Api
{
    public partial class Program;
}
