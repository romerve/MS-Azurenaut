namespace Checkout.Api;

public sealed class CheckoutCalculator
{
    public CheckoutResponse Calculate(IReadOnlyList<CartItem> items, decimal discountPercent)
    {
        var subtotal = items.Sum(item => checked(item.Quantity * item.UnitPrice));
        var discount = decimal.Round(
            subtotal * (discountPercent / 100m),
            2,
            MidpointRounding.AwayFromZero);
        var total = decimal.Round(subtotal - discount, 2, MidpointRounding.AwayFromZero);

        return new CheckoutResponse(subtotal, discount, total);
    }
}
