using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;

namespace Api.Contract.Tests;

public sealed class LegacyApiFactory : WebApplicationFactory<Legacy.Ordering.Api.Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"legacy-contract-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Orders", $"Data Source={_databasePath}");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}

public sealed class ModernApiFactory : IDisposable
{
    private readonly InventoryApiFactory _inventory = new();
    private readonly OrdersApiFactory _orders;
    private readonly FulfillmentApiFactory _fulfillment;
    private readonly EdgeApiFactory _edge;

    public ModernApiFactory()
    {
        _orders = new OrdersApiFactory(_inventory.CreateClient());
        _fulfillment = new FulfillmentApiFactory(
            _orders.CreateClient(),
            _inventory.CreateClient());
        _edge = new EdgeApiFactory(
            _orders.CreateClient(),
            _inventory.CreateClient(),
            _fulfillment.CreateClient());
    }

    public HttpClient CreateClient() => _edge.CreateClient();

    public void Dispose()
    {
        _edge.Dispose();
        _fulfillment.Dispose();
        _orders.Dispose();
        _inventory.Dispose();
    }
}

internal sealed class InventoryApiFactory : SqliteApiFactory<Inventory.Api.Program>
{
    protected override string ConnectionStringName => "Inventory";
}

internal sealed class OrdersApiFactory(HttpClient inventoryClient)
    : SqliteApiFactory<Orders.Api.Program>
{
    protected override string ConnectionStringName => "Orders";

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<Orders.Api.IInventoryAvailabilityClient>();
        services.AddSingleton<Orders.Api.IInventoryAvailabilityClient>(
            new OrdersInventoryBridge(inventoryClient));
    }
}

internal sealed class FulfillmentApiFactory(
    HttpClient ordersClient,
    HttpClient inventoryClient)
    : SqliteApiFactory<Fulfillment.Api.Program>
{
    protected override string ConnectionStringName => "Fulfillment";

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<Fulfillment.Api.IOrdersClient>();
        services.RemoveAll<Fulfillment.Api.IInventoryClient>();
        services.AddSingleton<Fulfillment.Api.IOrdersClient>(
            new FulfillmentOrdersBridge(ordersClient));
        services.AddSingleton<Fulfillment.Api.IInventoryClient>(
            new FulfillmentInventoryBridge(inventoryClient));
    }
}

internal sealed class EdgeApiFactory(
    HttpClient ordersClient,
    HttpClient inventoryClient,
    HttpClient fulfillmentClient)
    : WebApplicationFactory<Edge.Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(
            services =>
            {
                services.RemoveAll<Edge.Api.IOrdersClient>();
                services.RemoveAll<Edge.Api.IInventoryClient>();
                services.RemoveAll<Edge.Api.IFulfillmentClient>();
                services.AddSingleton<Edge.Api.IOrdersClient>(
                    new EdgeOrdersBridge(ordersClient));
                services.AddSingleton<Edge.Api.IInventoryClient>(
                    new EdgeInventoryBridge(inventoryClient));
                services.AddSingleton<Edge.Api.IFulfillmentClient>(
                    new EdgeFulfillmentBridge(fulfillmentClient));
            });
    }
}

internal abstract class SqliteApiFactory<TProgram> : WebApplicationFactory<TProgram>
    where TProgram : class
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"{typeof(TProgram).Name}-{Guid.NewGuid():N}.db");

    protected abstract string ConnectionStringName { get; }

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting(
            $"ConnectionStrings:{ConnectionStringName}",
            $"Data Source={_databasePath}");
        builder.ConfigureTestServices(ConfigureServices);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}

internal sealed class OrdersInventoryBridge(HttpClient client)
    : Orders.Api.IInventoryAvailabilityClient
{
    public async Task<bool> CanReserveAsync(
        string sku,
        int quantity,
        CancellationToken cancellationToken)
    {
        var response = await client.GetFromJsonAsync<Availability>(
            $"/internal/inventory/{sku}/availability?quantity={quantity}",
            cancellationToken);
        return response?.Available ?? false;
    }

    private sealed record Availability(bool Available);
}

internal sealed class FulfillmentOrdersBridge(HttpClient client)
    : Fulfillment.Api.IOrdersClient
{
    public Task<Fulfillment.Api.OrderResponse?> GetAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        GetOrderAsync($"/internal/orders/{id}", cancellationToken);

    public Task<Fulfillment.Api.OrderResponse?> MarkFulfilledAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        GetOrderAsync($"/internal/orders/{id}/fulfilled", cancellationToken, post: true);

    private async Task<Fulfillment.Api.OrderResponse?> GetOrderAsync(
        string path,
        CancellationToken cancellationToken,
        bool post = false)
    {
        using var response = post
            ? await client.PostAsync(path, content: null, cancellationToken)
            : await client.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Fulfillment.Api.OrderResponse>(
            cancellationToken);
    }
}

internal sealed class FulfillmentInventoryBridge(HttpClient client)
    : Fulfillment.Api.IInventoryClient
{
    public async Task ReserveAsync(
        string sku,
        Guid reservationId,
        int quantity,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            $"/internal/inventory/{sku}/reservations",
            new { reservationId, quantity },
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Insufficient inventory.");
        }

        response.EnsureSuccessStatusCode();
    }
}

internal sealed class EdgeOrdersBridge(HttpClient client) : Edge.Api.IOrdersClient
{
    public async Task<IReadOnlyList<Edge.Api.OrderResponse>> GetAllAsync(
        CancellationToken cancellationToken) =>
        await client.GetFromJsonAsync<IReadOnlyList<Edge.Api.OrderResponse>>(
            "/internal/orders",
            cancellationToken)
        ?? Array.Empty<Edge.Api.OrderResponse>();

    public async Task<Edge.Api.OrderResponse?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/internal/orders/{id}", cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.Content.ReadFromJsonAsync<Edge.Api.OrderResponse>(
                cancellationToken);
    }

    public async Task<Edge.Api.DownstreamResult<Edge.Api.OrderResponse>> CreateAsync(
        Edge.Api.CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "/internal/orders",
            request,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new(Edge.Api.DownstreamStatus.Conflict);
        }

        response.EnsureSuccessStatusCode();
        return new(
            Edge.Api.DownstreamStatus.Success,
            await response.Content.ReadFromJsonAsync<Edge.Api.OrderResponse>(
                cancellationToken));
    }
}

internal sealed class EdgeInventoryBridge(HttpClient client) : Edge.Api.IInventoryClient
{
    public async Task<Edge.Api.InventoryResponse?> GetAsync(
        string sku,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(
            $"/internal/inventory/{sku}",
            cancellationToken);
        return response.StatusCode == HttpStatusCode.NotFound
            ? null
            : await response.Content.ReadFromJsonAsync<Edge.Api.InventoryResponse>(
                cancellationToken);
    }
}

internal sealed class EdgeFulfillmentBridge(HttpClient client)
    : Edge.Api.IFulfillmentClient
{
    public async Task<Edge.Api.DownstreamResult<Edge.Api.OrderResponse>> FulfillAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(
            $"/internal/fulfillments/{orderId}",
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new(Edge.Api.DownstreamStatus.NotFound);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new(Edge.Api.DownstreamStatus.Conflict, Error: "Insufficient inventory.");
        }

        response.EnsureSuccessStatusCode();
        return new(
            Edge.Api.DownstreamStatus.Success,
            await response.Content.ReadFromJsonAsync<Edge.Api.OrderResponse>(
                cancellationToken));
    }
}
