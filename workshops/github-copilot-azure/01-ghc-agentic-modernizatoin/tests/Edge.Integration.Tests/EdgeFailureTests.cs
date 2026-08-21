using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Edge.Integration.Tests;

public sealed class UnavailableBackendsFactory : WebApplicationFactory<Edge.Api.Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Services:Orders", "http://127.0.0.1:9");
        builder.UseSetting("Services:Inventory", "http://127.0.0.1:9");
        builder.UseSetting("Services:Fulfillment", "http://127.0.0.1:9");
    }
}

public sealed class EdgeFailureTests
{
    [Fact]
    public async Task Order_creation_is_not_retried_when_outcome_is_unknown()
    {
        var handler = new CountingUnavailableHandler();
        using var factory = new WebApplicationFactory<Edge.Api.Program>()
            .WithWebHostBuilder(
                builder =>
                    builder.ConfigureServices(
                        services =>
                            services
                                .AddHttpClient("Edge.Orders.Write")
                                .ConfigurePrimaryHttpMessageHandler(() => handler)));
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/orders",
            new { customerId = "CUST-RETRY", sku = "BLUE-LAMP", quantity = 1 });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task Fulfillment_unavailable_returns_explicit_service_unavailable()
    {
        using var factory = new UnavailableBackendsFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/orders/11111111-1111-1111-1111-111111111111/fulfill",
            content: null);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Contains("unavailable", problem.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Edge_liveness_does_not_depend_on_backends()
    {
        using var factory = new UnavailableBackendsFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

public sealed class CountingUnavailableHandler : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
