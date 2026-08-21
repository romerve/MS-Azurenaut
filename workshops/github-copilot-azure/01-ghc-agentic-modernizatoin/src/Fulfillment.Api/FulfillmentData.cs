using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Fulfillment.Api;

public enum FulfillmentState
{
    Started,
    InventoryReserved,
    Completed
}

public sealed class FulfillmentProcess
{
    private FulfillmentProcess()
    {
    }

    public FulfillmentProcess(Guid orderId)
    {
        OrderId = orderId;
        State = FulfillmentState.Started;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid OrderId { get; private set; }
    public FulfillmentState State { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void Advance(FulfillmentState state)
    {
        if (state > State)
        {
            State = state;
            UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}

public sealed class FulfillmentDbContext(DbContextOptions<FulfillmentDbContext> options)
    : DbContext(options)
{
    public DbSet<FulfillmentProcess> Processes => Set<FulfillmentProcess>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var process = modelBuilder.Entity<FulfillmentProcess>();
        process.HasKey(item => item.OrderId);
        process.Property(item => item.State).IsConcurrencyToken();
    }
}

public sealed class FulfillmentService(
    FulfillmentDbContext database,
    IOrdersClient orders,
    IInventoryClient inventory,
    ILogger<FulfillmentService> logger)
{
    public async Task<OrderResponse?> FulfillAsync(
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (order.Status == "Fulfilled")
        {
            await SaveStateAsync(orderId, FulfillmentState.Completed, cancellationToken);
            return order;
        }

        await SaveStateAsync(orderId, FulfillmentState.Started, cancellationToken);
        await inventory.ReserveAsync(
            order.Sku,
            orderId,
            order.Quantity,
            cancellationToken);
        await SaveStateAsync(
            orderId,
            FulfillmentState.InventoryReserved,
            cancellationToken);

        var fulfilled = await orders.MarkFulfilledAsync(orderId, cancellationToken);
        if (fulfilled is null)
        {
            throw new InvalidOperationException("Order disappeared during fulfillment.");
        }

        await SaveStateAsync(orderId, FulfillmentState.Completed, cancellationToken);
        logger.LogInformation(
            "Completed fulfillment {OrderId} for {Quantity} unit(s) of {Sku}",
            orderId,
            order.Quantity,
            order.Sku);
        return fulfilled;
    }

    private async Task SaveStateAsync(
        Guid orderId,
        FulfillmentState state,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            database.ChangeTracker.Clear();
            var process = await database.Processes.SingleOrDefaultAsync(
                item => item.OrderId == orderId,
                cancellationToken);
            if (process is null)
            {
                process = new FulfillmentProcess(orderId);
                database.Processes.Add(process);
            }

            process.Advance(state);
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (
                attempt < maxAttempts
                && exception is DbUpdateConcurrencyException or DbUpdateException)
            {
                logger.LogInformation(
                    "Retrying fulfillment state {State} for {OrderId} after contention",
                    state,
                    orderId);
            }
        }

        throw new InvalidOperationException(
            "Fulfillment state changed repeatedly. Retry the request.");
    }
}

public static class FulfillmentDatabase
{
    public static IServiceCollection AddFulfillmentDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Fulfillment")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Fulfillment is required.");
        services.AddDbContext<FulfillmentDbContext>(
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

public static class FulfillmentBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<FulfillmentDbContext>();
        await database.Database.EnsureCreatedAsync();
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
