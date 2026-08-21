using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Orders.Api;

public enum OrderStatus
{
    Pending,
    Fulfilled
}

public sealed class Order
{
    private Order()
    {
    }

    public Order(
        Guid id,
        string customerId,
        string sku,
        int quantity,
        OrderStatus status,
        DateTimeOffset createdAt)
    {
        Id = id;
        CustomerId = customerId;
        Sku = sku;
        Quantity = quantity;
        Status = status;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string CustomerId { get; private set; } = string.Empty;
    public string Sku { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkFulfilled() => Status = OrderStatus.Fulfilled;
}

public sealed record CreateOrderRequest(string CustomerId, string Sku, int Quantity);

public sealed record OrderResponse(
    Guid Id,
    string CustomerId,
    string Sku,
    int Quantity,
    string Status);

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options)
    : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();
        order.HasKey(entity => entity.Id);
        order.Property(entity => entity.Status).IsConcurrencyToken();
    }
}

public sealed class OrderService(
    OrdersDbContext database,
    IInventoryAvailabilityClient inventory,
    ILogger<OrderService> logger)
{
    public async Task<IReadOnlyList<OrderResponse>> GetAllAsync(
        CancellationToken cancellationToken) =>
        (await database.Orders.AsNoTracking().ToListAsync(cancellationToken))
        .OrderBy(order => order.CreatedAt)
        .Select(ToResponse)
        .ToList();

    public async Task<OrderResponse?> GetAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var order = await database.Orders.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == id,
            cancellationToken);
        return order is null ? null : ToResponse(order);
    }

    public async Task<OrderResponse> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var normalizedSku = request.Sku.ToUpperInvariant();
        if (!await inventory.CanReserveAsync(
                normalizedSku,
                request.Quantity,
                cancellationToken))
        {
            throw new InvalidOperationException("Insufficient inventory.");
        }

        var order = new Order(
            Guid.NewGuid(),
            request.CustomerId,
            normalizedSku,
            request.Quantity,
            OrderStatus.Pending,
            DateTimeOffset.UtcNow);
        database.Orders.Add(order);
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Created order {OrderId} for customer {CustomerId} and SKU {Sku}",
            order.Id,
            order.CustomerId,
            order.Sku);
        return ToResponse(order);
    }

    public async Task<OrderResponse?> MarkFulfilledAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            database.ChangeTracker.Clear();
            var order = await database.Orders.SingleOrDefaultAsync(
                candidate => candidate.Id == id,
                cancellationToken);
            if (order is null)
            {
                return null;
            }

            if (order.Status == OrderStatus.Fulfilled)
            {
                return ToResponse(order);
            }

            order.MarkFulfilled();
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return ToResponse(order);
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                logger.LogInformation(
                    "Retrying order {OrderId} fulfillment transition after contention",
                    id);
            }
        }

        throw new InvalidOperationException(
            "Order changed repeatedly during fulfillment. Retry the request.");
    }

    private static OrderResponse ToResponse(Order order) =>
        new(
            order.Id,
            order.CustomerId,
            order.Sku,
            order.Quantity,
            order.Status.ToString());
}

public static class OrdersDatabase
{
    public static IServiceCollection AddOrdersDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Orders")
            ?? throw new InvalidOperationException("ConnectionStrings:Orders is required.");
        services.AddDbContext<OrdersDbContext>(
            options =>
            {
                if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
                {
                    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
                }
                else
                {
                    options.UseSqlite(connectionString);
                }
            });
        return services;
    }
}

public static class OrdersBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await database.Database.EnsureCreatedAsync();
        if (await database.Orders.AnyAsync())
        {
            return;
        }

        database.Orders.Add(
            new Order(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "CUST-100",
                "RED-CHAIR",
                2,
                OrderStatus.Pending,
                DateTimeOffset.Parse("2026-01-15T12:00:00+00:00")));
        await database.SaveChangesAsync();
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
