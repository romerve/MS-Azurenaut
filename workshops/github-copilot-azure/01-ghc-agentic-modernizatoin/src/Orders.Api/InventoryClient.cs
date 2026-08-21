using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;

namespace Orders.Api;

public interface IInventoryAvailabilityClient
{
    Task<bool> CanReserveAsync(
        string sku,
        int quantity,
        CancellationToken cancellationToken);
}

public sealed class InventoryAvailabilityClient(HttpClient client)
    : IInventoryAvailabilityClient
{
    public async Task<bool> CanReserveAsync(
        string sku,
        int quantity,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetFromJsonAsync<AvailabilityResponse>(
                $"/internal/inventory/{Uri.EscapeDataString(sku)}/availability?quantity={quantity}",
                cancellationToken);
            return response?.Available ?? false;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DownstreamUnavailableException("Inventory", exception);
        }
    }

    private sealed record AvailabilityResponse(bool Available);
}

public sealed class DownstreamUnavailableException(string serviceName, Exception innerException)
    : Exception($"{serviceName} service is unavailable.", innerException);

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

public static class InventoryClientRegistration
{
    public static IServiceCollection AddInventoryClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var url = configuration["Services:Inventory"] ?? "http://inventory:8080";
        services
            .AddHttpClient<IInventoryAvailabilityClient, InventoryAvailabilityClient>(
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
        return services;
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
