using Microsoft.EntityFrameworkCore;

namespace Legacy.Ordering.Api;

public sealed class LegacyOrder
{
    public Guid Id { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LegacyInventoryItem
{
    public string Sku { get; set; } = string.Empty;
    public int Available { get; set; }
}

public sealed class LegacyDbContext : DbContext
{
    public LegacyDbContext(DbContextOptions<LegacyDbContext> options)
        : base(options)
    {
    }

    public DbSet<LegacyOrder> Orders => Set<LegacyOrder>();
    public DbSet<LegacyInventoryItem> Inventory => Set<LegacyInventoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LegacyOrder>().HasKey(order => order.Id);
        modelBuilder.Entity<LegacyInventoryItem>().HasKey(item => item.Sku);
    }
}

public static class LegacySeed
{
    public static async Task InitializeAsync(LegacyDbContext database)
    {
        await database.Database.EnsureCreatedAsync();
        if (await database.Inventory.AnyAsync())
        {
            return;
        }

        database.Inventory.AddRange(
            new LegacyInventoryItem { Sku = "RED-CHAIR", Available = 25 },
            new LegacyInventoryItem { Sku = "BLUE-LAMP", Available = 12 });
        database.Orders.Add(
            new LegacyOrder
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                CustomerId = "CUST-100",
                Sku = "RED-CHAIR",
                Quantity = 2,
                Status = "Pending",
                CreatedAt = DateTimeOffset.Parse("2026-01-15T12:00:00Z")
            });

        await database.SaveChangesAsync();
    }
}
