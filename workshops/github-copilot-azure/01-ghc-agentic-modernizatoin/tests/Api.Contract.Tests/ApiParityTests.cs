using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Api.Contract.Tests;

public sealed class ApiParityTests
{
    [Fact]
    public async Task Seeded_read_contracts_match()
    {
        using var legacyFactory = new LegacyApiFactory();
        using var modernFactory = new ModernApiFactory();
        using var legacy = legacyFactory.CreateClient();
        using var modern = modernFactory.CreateClient();

        var legacyOrders = await legacy.GetStringAsync("/api/orders");
        var modernOrders = await modern.GetStringAsync("/api/orders");
        var legacyInventory = await legacy.GetStringAsync("/api/inventory/RED-CHAIR");
        var modernInventory = await modern.GetStringAsync("/api/inventory/RED-CHAIR");

        Assert.Equal(JsonNode.Parse(legacyOrders)!.ToJsonString(), JsonNode.Parse(modernOrders)!.ToJsonString());
        Assert.Equal(
            JsonNode.Parse(legacyInventory)!.ToJsonString(),
            JsonNode.Parse(modernInventory)!.ToJsonString());
    }

    [Fact]
    public async Task Create_and_fulfill_contracts_match()
    {
        using var legacyFactory = new LegacyApiFactory();
        using var modernFactory = new ModernApiFactory();
        using var legacy = legacyFactory.CreateClient();
        using var modern = modernFactory.CreateClient();
        var request = new { customerId = "CUST-200", sku = "BLUE-LAMP", quantity = 3 };

        var legacyCreate = await legacy.PostAsJsonAsync("/api/orders", request);
        var modernCreate = await modern.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, legacyCreate.StatusCode);
        Assert.Equal(legacyCreate.StatusCode, modernCreate.StatusCode);
        Assert.StartsWith("/api/orders/", legacyCreate.Headers.Location!.OriginalString);
        Assert.StartsWith("/api/orders/", modernCreate.Headers.Location!.OriginalString);

        var legacyOrder = await legacyCreate.Content.ReadFromJsonAsync<JsonObject>();
        var modernOrder = await modernCreate.Content.ReadFromJsonAsync<JsonObject>();
        AssertEquivalentOrder(legacyOrder!, modernOrder!, expectedStatus: "Pending");

        var legacyFulfill =
            await legacy.PostAsync($"/api/orders/{legacyOrder!["id"]}/fulfill", content: null);
        var modernFulfill =
            await modern.PostAsync($"/api/orders/{modernOrder!["id"]}/fulfill", content: null);
        Assert.Equal(HttpStatusCode.OK, legacyFulfill.StatusCode);
        Assert.Equal(legacyFulfill.StatusCode, modernFulfill.StatusCode);

        AssertEquivalentOrder(
            (await legacyFulfill.Content.ReadFromJsonAsync<JsonObject>())!,
            (await modernFulfill.Content.ReadFromJsonAsync<JsonObject>())!,
            expectedStatus: "Fulfilled");

        Assert.Equal(
            await legacy.GetStringAsync("/api/inventory/BLUE-LAMP"),
            await modern.GetStringAsync("/api/inventory/BLUE-LAMP"));
    }

    [Fact]
    public async Task Validation_and_conflict_status_codes_match()
    {
        using var legacyFactory = new LegacyApiFactory();
        using var modernFactory = new ModernApiFactory();
        using var legacy = legacyFactory.CreateClient();
        using var modern = modernFactory.CreateClient();

        var invalid = new { customerId = "", sku = "", quantity = 0 };
        var impossible = new { customerId = "CUST-300", sku = "BLUE-LAMP", quantity = 1000 };

        var legacyInvalid = await legacy.PostAsJsonAsync("/api/orders", invalid);
        var modernInvalid = await modern.PostAsJsonAsync("/api/orders", invalid);
        Assert.Equal(legacyInvalid.StatusCode, modernInvalid.StatusCode);
        Assert.Equal(
            JsonNode.Parse(await legacyInvalid.Content.ReadAsStringAsync())!.ToJsonString(),
            JsonNode.Parse(await modernInvalid.Content.ReadAsStringAsync())!.ToJsonString());
        Assert.Equal(
            "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            (await modernInvalid.Content.ReadFromJsonAsync<JsonObject>())!["type"]!.GetValue<string>());

        var legacyConflict = await legacy.PostAsJsonAsync("/api/orders", impossible);
        var modernConflict = await modern.PostAsJsonAsync("/api/orders", impossible);
        Assert.Equal(legacyConflict.StatusCode, modernConflict.StatusCode);
        Assert.Equal(
            JsonNode.Parse(await legacyConflict.Content.ReadAsStringAsync())!.ToJsonString(),
            JsonNode.Parse(await modernConflict.Content.ReadAsStringAsync())!.ToJsonString());
        Assert.Equal(
            "https://tools.ietf.org/html/rfc7231#section-6.5.8",
            (await modernConflict.Content.ReadFromJsonAsync<JsonObject>())!["type"]!.GetValue<string>());
    }

    [Fact]
    public async Task Repeated_fulfillment_is_idempotent_in_both_apis()
    {
        using var legacyFactory = new LegacyApiFactory();
        using var modernFactory = new ModernApiFactory();
        using var legacy = legacyFactory.CreateClient();
        using var modern = modernFactory.CreateClient();
        const string fulfillPath =
            "/api/orders/11111111-1111-1111-1111-111111111111/fulfill";

        await legacy.PostAsync(fulfillPath, content: null);
        await modern.PostAsync(fulfillPath, content: null);
        var legacySecond = await legacy.PostAsync(fulfillPath, content: null);
        var modernSecond = await modern.PostAsync(fulfillPath, content: null);

        Assert.Equal(legacySecond.StatusCode, modernSecond.StatusCode);
        AssertEquivalentOrder(
            (await legacySecond.Content.ReadFromJsonAsync<JsonObject>())!,
            (await modernSecond.Content.ReadFromJsonAsync<JsonObject>())!,
            expectedStatus: "Fulfilled");
        Assert.Equal(
            await legacy.GetStringAsync("/api/inventory/RED-CHAIR"),
            await modern.GetStringAsync("/api/inventory/RED-CHAIR"));
        Assert.Contains("\"available\":23", await modern.GetStringAsync("/api/inventory/RED-CHAIR"));
    }

    [Fact]
    public async Task Unknown_order_routes_return_matching_not_found_responses()
    {
        using var legacyFactory = new LegacyApiFactory();
        using var modernFactory = new ModernApiFactory();
        using var legacy = legacyFactory.CreateClient();
        using var modern = modernFactory.CreateClient();
        var unknownId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var legacyGet = await legacy.GetAsync($"/api/orders/{unknownId}");
        var modernGet = await modern.GetAsync($"/api/orders/{unknownId}");
        var legacyFulfill =
            await legacy.PostAsync($"/api/orders/{unknownId}/fulfill", content: null);
        var modernFulfill =
            await modern.PostAsync($"/api/orders/{unknownId}/fulfill", content: null);

        Assert.Equal(HttpStatusCode.NotFound, legacyGet.StatusCode);
        Assert.Equal(legacyGet.StatusCode, modernGet.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, legacyFulfill.StatusCode);
        Assert.Equal(legacyFulfill.StatusCode, modernFulfill.StatusCode);
        Assert.Equal(
            await legacyGet.Content.ReadAsStringAsync(),
            await modernGet.Content.ReadAsStringAsync());
        Assert.Equal(
            await legacyFulfill.Content.ReadAsStringAsync(),
            await modernFulfill.Content.ReadAsStringAsync());
    }

    private static void AssertEquivalentOrder(
        JsonObject legacy,
        JsonObject modern,
        string expectedStatus)
    {
        foreach (var property in new[] { "customerId", "sku", "quantity", "status" })
        {
            Assert.Equal(legacy[property]!.ToJsonString(), modern[property]!.ToJsonString());
        }

        Assert.Equal(expectedStatus, legacy["status"]!.GetValue<string>());
        Assert.NotEqual(Guid.Empty, Guid.Parse(legacy["id"]!.GetValue<string>()));
        Assert.NotEqual(Guid.Empty, Guid.Parse(modern["id"]!.GetValue<string>()));
    }
}
