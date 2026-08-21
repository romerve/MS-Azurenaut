using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Checkout.Api.Tests;

internal sealed class CheckoutApiFactory : WebApplicationFactory<Program>
{
    public ManualTimeProvider Clock { get; } =
        new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("CheckoutRateLimit:PermitLimit", "2");
        builder.UseSetting("CheckoutRateLimit:WindowSeconds", "60");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}
