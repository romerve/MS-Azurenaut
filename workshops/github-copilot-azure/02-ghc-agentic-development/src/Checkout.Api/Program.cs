using Checkout.Api;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<CheckoutRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(CheckoutRateLimitOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IClientRateLimiter, FixedWindowClientRateLimiter>();
builder.Services.AddSingleton<CheckoutCalculator>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapPost("/checkout", (
    HttpContext context,
    CheckoutRequest request,
    CheckoutCalculator calculator,
    IClientRateLimiter rateLimiter) =>
{
    var errors = Validate(request, context.Request.Headers["X-Client-Id"].ToString());
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var clientId = context.Request.Headers["X-Client-Id"].ToString().Trim();
    var decision = rateLimiter.TryAcquire(clientId);
    if (!decision.IsAllowed)
    {
        context.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString();
        return Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Checkout rate limit exceeded",
            detail: "Retry after the number of seconds provided in the Retry-After header.");
    }

    return Results.Ok(calculator.Calculate(request.Items!, request.DiscountPercent));
});

app.Run();

static Dictionary<string, string[]> Validate(CheckoutRequest request, string clientId)
{
    const int maxItems = 1_000;
    const int maxQuantity = 1_000_000;
    const decimal maxUnitPrice = 1_000_000m;
    var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

    if (string.IsNullOrWhiteSpace(clientId))
    {
        errors["X-Client-Id"] = ["A non-empty X-Client-Id header is required."];
    }
    else if (clientId.Length > 128)
    {
        errors["X-Client-Id"] = ["X-Client-Id must be 128 characters or fewer."];
    }

    if (request.Items is null || request.Items.Count == 0)
    {
        errors["items"] = ["At least one cart item is required."];
    }
    else if (request.Items.Count > maxItems)
    {
        errors["items"] = [$"A cart can contain at most {maxItems} items."];
    }
    else if (request.Items.Any(item =>
        item is null ||
        item.Quantity is <= 0 or > maxQuantity ||
        item.UnitPrice is < 0 or > maxUnitPrice))
    {
        errors["items"] =
        [
            $"Every item requires quantity 1–{maxQuantity} and unit price 0–{maxUnitPrice}."
        ];
    }

    if (request.DiscountPercent is < 0 or > 100)
    {
        errors["discountPercent"] = ["Discount percent must be between 0 and 100."];
    }

    return errors;
}

public partial class Program;
