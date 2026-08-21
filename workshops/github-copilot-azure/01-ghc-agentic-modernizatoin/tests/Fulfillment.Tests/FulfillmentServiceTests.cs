using Fulfillment.Api;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fulfillment.Tests;

public sealed class FulfillmentServiceTests
{
    [Fact]
    public async Task Completes_flow_and_uses_order_id_as_inventory_reservation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var database = CreateDatabase(connection);
        await database.Database.EnsureCreatedAsync();
        var orderId = Guid.NewGuid();
        var orders = new OrdersStub(orderId);
        var inventory = new InventoryStub();
        var service = new FulfillmentService(
            database,
            orders,
            inventory,
            NullLogger<FulfillmentService>.Instance);

        var result = await service.FulfillAsync(orderId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Fulfilled", result.Status);
        Assert.Equal(orderId, inventory.ReservationId);
        Assert.Equal(
            FulfillmentState.Completed,
            (await database.Processes.SingleAsync()).State);
    }

    [Fact]
    public async Task Propagates_backend_unavailable_failure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var database = CreateDatabase(connection);
        await database.Database.EnsureCreatedAsync();
        var service = new FulfillmentService(
            database,
            new UnavailableOrdersStub(),
            new InventoryStub(),
            NullLogger<FulfillmentService>.Instance);

        await Assert.ThrowsAsync<DownstreamUnavailableException>(
            () => service.FulfillAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static FulfillmentDbContext CreateDatabase(SqliteConnection connection) =>
        new(
            new DbContextOptionsBuilder<FulfillmentDbContext>()
                .UseSqlite(connection)
                .Options);

    private sealed class OrdersStub(Guid orderId) : IOrdersClient
    {
        public Task<OrderResponse?> GetAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<OrderResponse?>(
                new(orderId, "CUST-200", "BLUE-LAMP", 3, "Pending"));

        public Task<OrderResponse?> MarkFulfilledAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult<OrderResponse?>(
                new(orderId, "CUST-200", "BLUE-LAMP", 3, "Fulfilled"));
    }

    private sealed class UnavailableOrdersStub : IOrdersClient
    {
        public Task<OrderResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            throw new DownstreamUnavailableException(
                "Orders",
                new HttpRequestException("connection refused"));

        public Task<OrderResponse?> MarkFulfilledAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class InventoryStub : IInventoryClient
    {
        public Guid ReservationId { get; private set; }

        public Task ReserveAsync(
            string sku,
            Guid reservationId,
            int quantity,
            CancellationToken cancellationToken)
        {
            ReservationId = reservationId;
            return Task.CompletedTask;
        }
    }
}
