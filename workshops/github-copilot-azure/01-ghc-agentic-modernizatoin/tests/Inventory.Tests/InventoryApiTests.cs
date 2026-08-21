using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Inventory.Tests;

public sealed class InventoryFactory : WebApplicationFactory<Inventory.Api.Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"inventory-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting(
            "ConnectionStrings:Inventory",
            $"Data Source={_databasePath}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        File.Delete(_databasePath);
    }
}

public sealed class InventoryApiTests
{
    [Fact]
    public async Task Reservation_is_idempotent_and_owned_by_inventory_service()
    {
        using var factory = new InventoryFactory();
        using var client = factory.CreateClient();
        var reservationId = Guid.NewGuid();
        var request = new { reservationId, quantity = 3 };

        var first = await client.PostAsJsonAsync(
            "/internal/inventory/BLUE-LAMP/reservations",
            request);
        var second = await client.PostAsJsonAsync(
            "/internal/inventory/BLUE-LAMP/reservations",
            request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var inventory = await client.GetFromJsonAsync<Inventory.Api.InventoryResponse>(
            "/internal/inventory/BLUE-LAMP");
        Assert.NotNull(inventory);
        Assert.Equal(9, inventory.Available);
    }

    [Fact]
    public async Task Insufficient_inventory_returns_conflict()
    {
        using var factory = new InventoryFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/internal/inventory/BLUE-LAMP/reservations",
            new { reservationId = Guid.NewGuid(), quantity = 1000 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
