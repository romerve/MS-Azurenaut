namespace Checkout.Api;

public sealed record CartItem(int Quantity, decimal UnitPrice);

public sealed record CheckoutRequest(IReadOnlyList<CartItem>? Items, decimal DiscountPercent = 0);

public sealed record CheckoutResponse(decimal Subtotal, decimal Discount, decimal Total);
