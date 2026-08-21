using Microsoft.EntityFrameworkCore;

namespace Legacy.Ordering.Api;

// This intentionally centralizes three business capabilities so the assessment
// has a realistic coupling seam to discover without introducing unsafe code.
public sealed class LegacyOrderService
{
    private readonly LegacyDbContext _database;
    private readonly IConfiguration _configuration;

    public LegacyOrderService(LegacyDbContext database, IConfiguration configuration)
    {
        _database = database;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<OrderResponse>> GetOrdersAsync()
    {
        var orders = await _database.Orders.AsNoTracking().ToListAsync();
        return orders
            .OrderBy(order => order.CreatedAt)
            .Select(ToResponse)
            .ToList();
    }

    public async Task<OrderResponse?> GetOrderAsync(Guid id)
    {
        var order = await _database.Orders.FindAsync(id);
        return order is null ? null : ToResponse(order);
    }

    public async Task<InventoryResponse?> GetInventoryAsync(string sku)
    {
        var item = await _database.Inventory.FindAsync(sku.ToUpperInvariant());
        return item is null ? null : new InventoryResponse(item.Sku, item.Available);
    }

    public async Task<OrderResponse> CreateOrderAsync(CreateOrderRequest request)
    {
        var normalizedSku = request.Sku.ToUpperInvariant();
        var inventory = await _database.Inventory.FindAsync(normalizedSku);
        if (inventory is null || inventory.Available < request.Quantity)
        {
            throw new InvalidOperationException("Insufficient inventory.");
        }

        var order = new LegacyOrder
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            Sku = normalizedSku,
            Quantity = request.Quantity,
            Status = "Pending",
            CreatedAt = DateTimeOffset.UtcNow
        };

        _database.Orders.Add(order);
        await _database.SaveChangesAsync();
        return ToResponse(order);
    }

    public async Task<OrderResponse?> FulfillAsync(Guid id)
    {
        var order = await _database.Orders.FindAsync(id);
        if (order is null)
        {
            return null;
        }

        if (order.Status == "Fulfilled")
        {
            return ToResponse(order);
        }

        var inventory = await _database.Inventory.FindAsync(order.Sku);
        if (inventory is null || inventory.Available < order.Quantity)
        {
            throw new InvalidOperationException("Insufficient inventory.");
        }

        // A synchronous configuration lookup stands in for a legacy downstream
        // fulfillment dependency that the guided modernization will isolate.
        _ = _configuration["LegacySettings:FulfillmentApiKey"];
        inventory.Available -= order.Quantity;
        order.Status = "Fulfilled";
        await _database.SaveChangesAsync();
        return ToResponse(order);
    }

    private static OrderResponse ToResponse(LegacyOrder order) =>
        new(order.Id, order.CustomerId, order.Sku, order.Quantity, order.Status);
}
