using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Orders.Tests;

public sealed class OrdersFactory(bool inventoryAvailable)
    : WebApplicationFactory<Orders.Api.Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"orders-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Orders", $"Data Source={_databasePath}");
        builder.ConfigureTestServices(
            services =>
            {
                services.RemoveAll<Orders.Api.IInventoryAvailabilityClient>();
                services.AddSingleton<Orders.Api.IInventoryAvailabilityClient>(
                    new InventoryStub(inventoryAvailable));
            });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_databasePath);
    }

    private sealed class InventoryStub(bool available)
        : Orders.Api.IInventoryAvailabilityClient
    {
        public Task<bool> CanReserveAsync(
            string sku,
            int quantity,
            CancellationToken cancellationToken) =>
            Task.FromResult(available);
    }
}

public sealed class OrdersApiTests
{
    [Fact]
    public async Task Creates_and_reads_order_in_owned_database()
    {
        using var factory = new OrdersFactory(inventoryAvailable: true);
        using var client = factory.CreateClient();

        var create = await client.PostAsJsonAsync(
            "/internal/orders",
            new { customerId = "CUST-200", sku = "blue-lamp", quantity = 3 });
        var order = await create.Content.ReadFromJsonAsync<Orders.Api.OrderResponse>();

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        Assert.NotNull(order);
        Assert.Equal("BLUE-LAMP", order.Sku);
        var read = await client.GetAsync($"/internal/orders/{order.Id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Rejects_order_when_inventory_service_reports_shortage()
    {
        using var factory = new OrdersFactory(inventoryAvailable: false);
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/internal/orders",
            new { customerId = "CUST-200", sku = "BLUE-LAMP", quantity = 3 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
