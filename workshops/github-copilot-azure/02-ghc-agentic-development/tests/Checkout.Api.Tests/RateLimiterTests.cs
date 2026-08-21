using Checkout.Api;
using Microsoft.Extensions.Options;

namespace Checkout.Api.Tests;

public sealed class RateLimiterTests
{
    [Fact]
    public void New_clients_fail_closed_when_active_tracking_capacity_is_exhausted()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var limiter = new FixedWindowClientRateLimiter(
            clock,
            Options.Create(new CheckoutRateLimitOptions
            {
                PermitLimit = 1,
                WindowSeconds = 60,
                MaxTrackedClients = 2
            }));

        Assert.True(limiter.TryAcquire("client-a").IsAllowed);
        Assert.True(limiter.TryAcquire("client-b").IsAllowed);

        var rejected = limiter.TryAcquire("rotated-client");

        Assert.False(rejected.IsAllowed);
        Assert.Equal(60, rejected.RetryAfterSeconds);

        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(limiter.TryAcquire("rotated-client").IsAllowed);
    }
}
