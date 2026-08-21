using System.ComponentModel.DataAnnotations;

namespace Checkout.Api;

public sealed class CheckoutRateLimitOptions
{
    public const string SectionName = "CheckoutRateLimit";

    [Range(1, 10_000)]
    public int PermitLimit { get; init; } = 3;

    [Range(1, 86_400)]
    public int WindowSeconds { get; init; } = 60;

    [Range(1, 100_000)]
    public int MaxTrackedClients { get; init; } = 10_000;
}
