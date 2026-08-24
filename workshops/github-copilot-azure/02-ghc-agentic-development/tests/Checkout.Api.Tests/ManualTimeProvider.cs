namespace Checkout.Api.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
{
    private DateTimeOffset currentTime = initialTime;

    public override DateTimeOffset GetUtcNow() => currentTime;

    public void Advance(TimeSpan duration) => currentTime += duration;
}
