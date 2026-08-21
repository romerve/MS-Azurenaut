using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api;

public sealed class InventoryConflictException(string message)
    : InvalidOperationException(message);

public sealed class InventoryItem
{
    private InventoryItem()
    {
    }

    public InventoryItem(string sku, int available)
    {
        Sku = sku;
        Available = available;
    }

    public string Sku { get; private set; } = string.Empty;
    public int Available { get; private set; }

    public bool CanReserve(int quantity) => quantity > 0 && Available >= quantity;

    public void Reserve(int quantity)
    {
        if (!CanReserve(quantity))
        {
            throw new InventoryConflictException("Insufficient inventory.");
        }

        Available -= quantity;
    }
}

public sealed class InventoryReservation
{
    private InventoryReservation()
    {
    }

    public InventoryReservation(Guid reservationId, string sku, int quantity)
    {
        ReservationId = reservationId;
        Sku = sku;
        Quantity = quantity;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid ReservationId { get; private set; }
    public string Sku { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options)
    : DbContext(options)
{
    public DbSet<InventoryItem> Inventory => Set<InventoryItem>();
    public DbSet<InventoryReservation> Reservations => Set<InventoryReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var inventory = modelBuilder.Entity<InventoryItem>();
        inventory.HasKey(item => item.Sku);
        inventory.Property(item => item.Available).IsConcurrencyToken();
        modelBuilder.Entity<InventoryReservation>().HasKey(
            reservation => reservation.ReservationId);
    }
}

public sealed record InventoryResponse(string Sku, int Available);
public sealed record AvailabilityResponse(bool Available);
public sealed record ReservationRequest(Guid ReservationId, int Quantity);

public sealed class InventoryService(
    InventoryDbContext database,
    ILogger<InventoryService> logger)
{
    public async Task<InventoryResponse?> GetAsync(
        string sku,
        CancellationToken cancellationToken)
    {
        var normalizedSku = sku.ToUpperInvariant();
        var item = await database.Inventory.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Sku == normalizedSku,
            cancellationToken);
        return item is null ? null : ToResponse(item);
    }

    public Task<bool> CanReserveAsync(
        string sku,
        int quantity,
        CancellationToken cancellationToken)
    {
        var normalizedSku = sku.ToUpperInvariant();
        return database.Inventory.AnyAsync(
            item => item.Sku == normalizedSku && item.Available >= quantity,
            cancellationToken);
    }

    public async Task<InventoryResponse> ReserveAsync(
        string sku,
        Guid reservationId,
        int quantity,
        CancellationToken cancellationToken)
    {
        var strategy = database.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(
            () => ReserveWithContentionRetriesAsync(
                sku,
                reservationId,
                quantity,
                cancellationToken));
    }

    private async Task<InventoryResponse> ReserveWithContentionRetriesAsync(
        string sku,
        Guid reservationId,
        int quantity,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        var normalizedSku = sku.ToUpperInvariant();
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var transaction =
                await database.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var existing = await database.Reservations.AsNoTracking().SingleOrDefaultAsync(
                    reservation => reservation.ReservationId == reservationId,
                    cancellationToken);
                var item = await database.Inventory.SingleOrDefaultAsync(
                    candidate => candidate.Sku == normalizedSku,
                    cancellationToken)
                    ?? throw new InventoryConflictException("Insufficient inventory.");
                if (existing is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                    return ToResponse(item);
                }

                item.Reserve(quantity);
                database.Reservations.Add(
                    new InventoryReservation(reservationId, normalizedSku, quantity));
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                logger.LogInformation(
                    "Reserved {Quantity} unit(s) of {Sku} for {ReservationId}",
                    quantity,
                    normalizedSku,
                    reservationId);
                return ToResponse(item);
            }
            catch (Exception exception) when (
                attempt < maxAttempts
                && exception is DbUpdateConcurrencyException or DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                database.ChangeTracker.Clear();
                logger.LogInformation(
                    "Retrying inventory reservation {ReservationId} after contention",
                    reservationId);
            }
        }

        throw new InventoryConflictException(
            "Inventory changed repeatedly. Retry the request.");
    }

    private static InventoryResponse ToResponse(InventoryItem item) =>
        new(item.Sku, item.Available);
}

public static class InventoryDatabase
{
    public static IServiceCollection AddInventoryDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Inventory")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Inventory is required.");
        services.AddDbContext<InventoryDbContext>(
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

public static class InventoryBootstrap
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await database.Database.EnsureCreatedAsync();
        if (await database.Inventory.AnyAsync())
        {
            return;
        }

        database.Inventory.AddRange(
            new InventoryItem("RED-CHAIR", 25),
            new InventoryItem("BLUE-LAMP", 12));
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
