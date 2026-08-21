using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace Edge.Api;

public interface IOrdersClient
{
    Task<IReadOnlyList<OrderResponse>> GetAllAsync(CancellationToken cancellationToken);
    Task<OrderResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<DownstreamResult<OrderResponse>> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken);
}

public interface IInventoryClient
{
    Task<InventoryResponse?> GetAsync(string sku, CancellationToken cancellationToken);
}

public interface IFulfillmentClient
{
    Task<DownstreamResult<OrderResponse>> FulfillAsync(
        Guid orderId,
        CancellationToken cancellationToken);
}

public sealed class OrdersClient(HttpClient readClient, HttpClient writeClient) : IOrdersClient
{
    public async Task<IReadOnlyList<OrderResponse>> GetAllAsync(
        CancellationToken cancellationToken) =>
        await ServiceClient.SendAsync<IReadOnlyList<OrderResponse>>(
            readClient,
            HttpMethod.Get,
            "/internal/orders",
            content: null,
            cancellationToken)
        ?? Array.Empty<OrderResponse>();

    public Task<OrderResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        ServiceClient.GetOptionalAsync<OrderResponse>(
            readClient,
            $"/internal/orders/{id}",
            cancellationToken);

    public async Task<DownstreamResult<OrderResponse>> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var response = await ServiceClient.SendRawAsync(
            writeClient,
            HttpMethod.Post,
            "/internal/orders",
            JsonContent.Create(request),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return new(DownstreamStatus.Conflict);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new DownstreamUnavailableException("Orders");
        }

        response.EnsureSuccessStatusCode();
        return new(
            DownstreamStatus.Success,
            await response.Content.ReadFromJsonAsync<OrderResponse>(cancellationToken));
    }
}

public sealed class InventoryClient(HttpClient httpClient) : IInventoryClient
{
    public Task<InventoryResponse?> GetAsync(string sku, CancellationToken cancellationToken) =>
        ServiceClient.GetOptionalAsync<InventoryResponse>(
            httpClient,
            $"/internal/inventory/{Uri.EscapeDataString(sku)}",
            cancellationToken);
}

public sealed class FulfillmentClient(HttpClient httpClient) : IFulfillmentClient
{
    public async Task<DownstreamResult<OrderResponse>> FulfillAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        using var response = await ServiceClient.SendRawAsync(
            httpClient,
            HttpMethod.Post,
            $"/internal/fulfillments/{orderId}",
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new(DownstreamStatus.NotFound);
        }

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(
                cancellationToken);
            return new(DownstreamStatus.Conflict, Error: problem?.Title);
        }

        if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            throw new DownstreamUnavailableException("Fulfillment");
        }

        response.EnsureSuccessStatusCode();
        return new(
            DownstreamStatus.Success,
            await response.Content.ReadFromJsonAsync<OrderResponse>(cancellationToken));
    }
}

public sealed class DownstreamUnavailableException(string serviceName, Exception? innerException = null)
    : Exception($"{serviceName} service is unavailable.", innerException)
{
    public string ServiceName { get; } = serviceName;
}

internal static class ServiceClient
{
    public static async Task<T?> GetOptionalAsync<T>(
        HttpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(
            client,
            HttpMethod.Get,
            path,
            content: null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public static async Task<T?> SendAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(
            client,
            method,
            path,
            content,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    public static async Task<HttpResponseMessage> SendRawAsync(
        HttpClient client,
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
            throw new DownstreamUnavailableException(
                client.BaseAddress?.Host ?? "downstream",
                exception);
        }
    }
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
        services.AddOrdersClient(configuration);
        services.AddServiceClient<IInventoryClient, InventoryClient>(
            configuration,
            "Inventory",
            "http://inventory:8080");
        services.AddServiceClient<IFulfillmentClient, FulfillmentClient>(
            configuration,
            "Fulfillment",
            "http://fulfillment:8080");
        return services;
    }

    private static void AddOrdersClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var url = configuration["Services:Orders"] ?? "http://orders:8080";
        services
            .AddHttpClient(
                "Edge.Orders.Read",
                client =>
                {
                    client.BaseAddress = new Uri(url);
                    client.Timeout = TimeSpan.FromSeconds(5);
                })
            .AddHttpMessageHandler<CorrelationHandler>()
            .AddStandardResilienceHandler(
                options =>
                {
                    options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
                    options.Retry.MaxRetryAttempts = 2;
                });
        services
            .AddHttpClient(
                "Edge.Orders.Write",
                client =>
                {
                    client.BaseAddress = new Uri(url);
                    client.Timeout = TimeSpan.FromSeconds(5);
                })
            .AddHttpMessageHandler<CorrelationHandler>();
        services.AddTransient<IOrdersClient>(
            provider =>
            {
                var factory = provider.GetRequiredService<IHttpClientFactory>();
                return new OrdersClient(
                    factory.CreateClient("Edge.Orders.Read"),
                    factory.CreateClient("Edge.Orders.Write"));
            });
    }

    private static void AddServiceClient<TClient, TImplementation>(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string defaultUrl)
        where TClient : class
        where TImplementation : class, TClient
    {
        var url = configuration[$"Services:{serviceName}"] ?? defaultUrl;
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

public static class CorrelationMiddleware
{
    public static IApplicationBuilder UseCorrelation(this IApplicationBuilder app) =>
        app.Use(
            async (context, next) =>
            {
                var correlationId =
                    context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                    ?? Activity.Current?.TraceId.ToString()
                    ?? Guid.NewGuid().ToString("N");
                context.TraceIdentifier = correlationId;
                context.Response.Headers["X-Correlation-ID"] = correlationId;
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("Correlation");
                using (logger.BeginScope(
                    new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
                {
                    await next();
                }
            });
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
                catch (HttpRequestException exception)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(
                        new ProblemDetails
                        {
                            Type = "https://tools.ietf.org/html/rfc7231#section-6.6.4",
                            Title = "A downstream service is unavailable.",
                            Detail = exception.Message,
                            Status = StatusCodes.Status503ServiceUnavailable
                        });
                }
            });
}

public static class PublicApiResults
{
    public static IResult ValidationProblem() =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                ["order"] =
                [
                    "CustomerId, Sku, and a positive Quantity are required."
                ]
            },
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");

    public static IResult Conflict(string title) =>
        Results.Conflict(
            new ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8",
                Title = title,
                Status = StatusCodes.Status409Conflict
            });
}
