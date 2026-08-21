using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Ordering.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = builder.Configuration.GetConnectionString("Orders")
            ?? throw new InvalidOperationException("ConnectionStrings:Orders is required.");

        builder.Services.AddDbContext<LegacyDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            }
            else
            {
                options.UseSqlite(connectionString);
            }
        });
        builder.Services.AddScoped<LegacyOrderService>();

        var app = builder.Build();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await LegacySeed.InitializeAsync(scope.ServiceProvider.GetRequiredService<LegacyDbContext>());
        }

        app.MapGet("/api/orders", (LegacyOrderService service) => service.GetOrdersAsync());

        app.MapGet(
            "/api/orders/{id:guid}",
            async (Guid id, LegacyOrderService service) =>
            {
                var order = await service.GetOrderAsync(id);
                return order is null ? Results.NotFound() : Results.Ok(order);
            });

        app.MapGet(
            "/api/inventory/{sku}",
            async (string sku, LegacyOrderService service) =>
            {
                var item = await service.GetInventoryAsync(sku);
                return item is null ? Results.NotFound() : Results.Ok(item);
            });

        app.MapPost(
            "/api/orders",
            async ([FromBody] CreateOrderRequest request, LegacyOrderService service) =>
            {
                if (string.IsNullOrWhiteSpace(request.CustomerId)
                    || string.IsNullOrWhiteSpace(request.Sku)
                    || request.Quantity <= 0)
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["order"] = new[] { "CustomerId, Sku, and a positive Quantity are required." }
                        },
                        type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
                }

                try
                {
                    var order = await service.CreateOrderAsync(request);
                    return Results.Created($"/api/orders/{order.Id}", order);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(
                        new ProblemDetails
                        {
                            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                            Title = exception.Message,
                            Status = 409
                        });
                }
            });

        app.MapPost(
            "/api/orders/{id:guid}/fulfill",
            async (Guid id, LegacyOrderService service) =>
            {
                try
                {
                    var order = await service.FulfillAsync(id);
                    return order is null ? Results.NotFound() : Results.Ok(order);
                }
                catch (InvalidOperationException exception)
                {
                    return Results.Conflict(
                        new ProblemDetails
                        {
                            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                            Title = exception.Message,
                            Status = 409
                        });
                }
            });

        await app.RunAsync();
    }
}
