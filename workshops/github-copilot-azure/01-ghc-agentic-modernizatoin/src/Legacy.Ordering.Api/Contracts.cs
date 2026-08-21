namespace Legacy.Ordering.Api;

public sealed record CreateOrderRequest(string CustomerId, string Sku, int Quantity);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string Sku,
    int Quantity,
    string Status);

public sealed record InventoryResponse(string Sku, int Available);
