using Microsoft.Extensions.Options;

namespace Checkout.Api;

public readonly record struct RateLimitDecision(bool IsAllowed, int RetryAfterSeconds);

public interface IClientRateLimiter
{
    RateLimitDecision TryAcquire(string clientId);
}

public sealed class FixedWindowClientRateLimiter(
    TimeProvider timeProvider,
    IOptions<CheckoutRateLimitOptions> options) : IClientRateLimiter
{
    private readonly object gate = new();
    private readonly Dictionary<string, WindowState> clients = new(StringComparer.Ordinal);
    private readonly CheckoutRateLimitOptions settings = options.Value;

    public RateLimitDecision TryAcquire(string clientId)
    {
        var now = timeProvider.GetUtcNow();
        var window = TimeSpan.FromSeconds(settings.WindowSeconds);

        lock (gate)
        {
            if (clients.TryGetValue(clientId, out var state))
            {
                if (now >= state.StartedAt + window)
                {
                    clients[clientId] = new WindowState(now, 1);
                    return new RateLimitDecision(true, 0);
                }

                if (state.Count < settings.PermitLimit)
                {
                    clients[clientId] = state with { Count = state.Count + 1 };
                    return new RateLimitDecision(true, 0);
                }

                return RejectUntil(state.StartedAt + window, now);
            }

            RemoveExpiredWindows(now, window);
            if (clients.Count >= settings.MaxTrackedClients)
            {
                var earliestReset = clients.Values.Min(candidate => candidate.StartedAt) + window;
                return RejectUntil(earliestReset, now);
            }

            clients[clientId] = new WindowState(now, 1);
            return new RateLimitDecision(true, 0);
        }
    }

    private void RemoveExpiredWindows(DateTimeOffset now, TimeSpan window)
    {
        foreach (var clientId in clients
            .Where(pair => now >= pair.Value.StartedAt + window)
            .Select(pair => pair.Key)
            .ToArray())
        {
            clients.Remove(clientId);
        }
    }

    private static RateLimitDecision RejectUntil(DateTimeOffset resetAt, DateTimeOffset now)
    {
        var retryAfter = Math.Max(1, (int)Math.Ceiling((resetAt - now).TotalSeconds));
        return new RateLimitDecision(false, retryAfter);
    }

    private sealed record WindowState(DateTimeOffset StartedAt, int Count);
}
