using System.Net;
using System.Net.Http.Json;
using Checkout.Api;
using Microsoft.AspNetCore.Http;

namespace Checkout.Api.Tests;

public sealed class CheckoutAcceptanceTests
{
    [Fact]
    public async Task Checkout_returns_calculated_total_for_ordinary_cart()
    {
        await using var factory = new CheckoutApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest("ordinary-client", new CheckoutRequest(
            [new CartItem(2, 12.50m), new CartItem(1, 5m)],
            10m));

        using var response = await client.SendAsync(request);
        var checkout = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new CheckoutResponse(30m, 3m, 27m), checkout);
    }

    [Fact]
    public async Task Third_request_from_same_client_returns_429_and_retry_after()
    {
        await using var factory = new CheckoutApiFactory();
        using var client = factory.CreateClient();

        using var first = await client.SendAsync(CreateRequest("limited-client"));
        using var second = await client.SendAsync(CreateRequest("limited-client"));
        using var rejected = await client.SendAsync(CreateRequest("limited-client"));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("60", rejected.Headers.RetryAfter?.ToString());
    }

    [Fact]
    public async Task Limits_are_independent_per_client_and_reset_after_window()
    {
        await using var factory = new CheckoutApiFactory();
        using var client = factory.CreateClient();

        using var a1 = await client.SendAsync(CreateRequest("client-a"));
        using var a2 = await client.SendAsync(CreateRequest("client-a"));
        using var b1 = await client.SendAsync(CreateRequest("client-b"));
        using var blocked = await client.SendAsync(CreateRequest("client-a"));

        factory.Clock.Advance(TimeSpan.FromSeconds(60));
        using var afterReset = await client.SendAsync(CreateRequest("client-a"));

        Assert.Equal(HttpStatusCode.OK, b1.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterReset.StatusCode);
    }

    [Fact]
    public async Task Invalid_request_returns_validation_problem_without_consuming_permit()
    {
        await using var factory = new CheckoutApiFactory();
        using var client = factory.CreateClient();

        using var invalid = await client.SendAsync(CreateRequest(
            "validation-client",
            new CheckoutRequest([], 101m)));
        var problem = await invalid.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        using var valid1 = await client.SendAsync(CreateRequest("validation-client"));
        using var valid2 = await client.SendAsync(CreateRequest("validation-client"));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("items", problem!.Errors.Keys);
        Assert.Contains("discountPercent", problem.Errors.Keys);
        Assert.Equal(HttpStatusCode.OK, valid1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid2.StatusCode);
    }

    [Fact]
    public async Task Missing_client_header_returns_validation_problem()
    {
        await using var factory = new CheckoutApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/checkout")
        {
            Content = JsonContent.Create(DefaultCheckout())
        };

        using var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("X-Client-Id", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Arithmetic_overflow_input_returns_validation_problem()
    {
        await using var factory = new CheckoutApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "overflow-client",
            new CheckoutRequest([new CartItem(2, decimal.MaxValue)]));

        using var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("items", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Null_cart_entry_returns_validation_problem()
    {
        await using var factory = new CheckoutApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/checkout")
        {
            Content = JsonContent.Create(new { items = new object?[] { null } })
        };
        request.Headers.Add("X-Client-Id", "null-entry-client");

        using var response = await client.SendAsync(request);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("items", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Large_cart_uses_decimal_arithmetic_without_overflow()
    {
        await using var factory = new CheckoutApiFactory();
        using var client = factory.CreateClient();
        using var request = CreateRequest(
            "large-cart-client",
            new CheckoutRequest([new CartItem(50_000, 50_000m)], 10m));

        using var response = await client.SendAsync(request);
        var checkout = await response.Content.ReadFromJsonAsync<CheckoutResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2_500_000_000m, checkout!.Subtotal);
        Assert.Equal(2_250_000_000m, checkout.Total);
    }

    private static HttpRequestMessage CreateRequest(
        string clientId,
        CheckoutRequest? checkout = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/checkout")
        {
            Content = JsonContent.Create(checkout ?? DefaultCheckout())
        };
        request.Headers.Add("X-Client-Id", clientId);
        return request;
    }

    private static CheckoutRequest DefaultCheckout() =>
        new([new CartItem(1, 10m)]);
}
