using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Api;

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string Sku,
    int Quantity,
    string Status);

public interface IOrdersClient
{
    Task<OrderResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<OrderResponse?> MarkFulfilledAsync(Guid id, CancellationToken cancellationToken);
}

public interface IInventoryClient
{
    Task ReserveAsync(
        string sku,
        Guid reservationId,
        int quantity,
        CancellationToken cancellationToken);
}

public sealed class OrdersClient(HttpClient client) : IOrdersClient
{
    public async Task<OrderResponse?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Get,
            $"/internal/orders/{id}",
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderResponse>(cancellationToken);
    }

    public async Task<OrderResponse?> MarkFulfilledAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            $"/internal/orders/{id}/fulfilled",
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrderResponse>(cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path) { Content = content };
            return await client.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownstreamUnavailableException("Orders", exception);
        }
    }
}

public sealed class InventoryClient(HttpClient client) : IInventoryClient
{
    public async Task ReserveAsync(
        string sku,
        Guid reservationId,
        int quantity,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(
                $"/internal/inventory/{Uri.EscapeDataString(sku)}/reservations",
                new { reservationId, quantity },
                cancellationToken);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
                    cancellationToken);
                throw new InvalidOperationException(
                    problem?.Title ?? "Insufficient inventory.");
            }

            response.EnsureSuccessStatusCode();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownstreamUnavailableException("Inventory", exception);
        }
    }
}

public sealed class DownstreamUnavailableException(string serviceName, Exception innerException)
    : Exception($"{serviceName} service is unavailable.", innerException)
{
    public string ServiceName { get; } = serviceName;
}

public sealed class CorrelationHandler(IHttpContextAccessor contextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId =
            contextAccessor.HttpContext?.TraceIdentifier
            ?? Activity.Current?.TraceId.ToString();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

public static class ServiceClientRegistration
{
    public static IServiceCollection AddServiceClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddResilientClient<IOrdersClient, OrdersClient>(
            configuration["Services:Orders"] ?? "http://orders:8080");
        services.AddResilientClient<IInventoryClient, InventoryClient>(
            configuration["Services:Inventory"] ?? "http://inventory:8080");
        return services;
    }

    private static void AddResilientClient<TClient, TImplementation>(
        this IServiceCollection services,
        string url)
        where TClient : class
        where TImplementation : class, TClient
    {
        services
            .AddHttpClient<TClient, TImplementation>(
                client =>
                {
                    client.BaseAddress = new Uri(url);
                    client.Timeout = TimeSpan.FromSeconds(5);
                })
            .AddHttpMessageHandler<CorrelationHandler>()
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
                options.Retry.MaxRetryAttempts = 2;
            });
    }
}

public static class DownstreamFailureMiddleware
{
    public static IApplicationBuilder UseDownstreamFailures(this IApplicationBuilder app) =>
        app.Use(
            async (context, next) =>
            {
                try
                {
                    await next();
                }
                catch (DownstreamUnavailableException exception)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(
                        new ProblemDetails
                        {
                            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                            Title = exception.Message,
                            Status = StatusCodes.Status503ServiceUnavailable
                        });
                }
            });
}
