using System.Net;
using System.Net.Http.Json;
using Legacy.Ordering.Api;

namespace Legacy.Characterization.Tests;

public sealed class LegacyBehaviorTests
{
    [Fact]
    public async Task Seeded_state_is_stable()
    {
        using var factory = new LegacyApiFactory();
        using var client = factory.CreateClient();

        var orders = await client.GetFromJsonAsync<List<OrderResponse>>("/api/orders");
        var inventory =
            await client.GetFromJsonAsync<InventoryResponse>("/api/inventory/RED-CHAIR");

        var order = Assert.Single(orders!);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), order.Id);
        Assert.Equal("CUST-100", order.CustomerId);
        Assert.Equal("Pending", order.Status);
        Assert.Equal(25, inventory!.Available);
    }

    [Fact]
    public async Task Fulfillment_is_idempotent_and_reserves_inventory_once()
    {
        using var factory = new LegacyApiFactory();
        using var client = factory.CreateClient();
        const string fulfillPath =
            "/api/orders/11111111-1111-1111-1111-111111111111/fulfill";

        var first = await client.PostAsync(fulfillPath, content: null);
        var second = await client.PostAsync(fulfillPath, content: null);
        var inventory =
            await client.GetFromJsonAsync<InventoryResponse>("/api/inventory/RED-CHAIR");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("Fulfilled", (await first.Content.ReadFromJsonAsync<OrderResponse>())!.Status);
        Assert.Equal(23, inventory!.Available);
    }
}
