using Microsoft.EntityFrameworkCore;
using VallianiDataSync.Api.Models;

namespace VallianiDataSync.Api.Data;

public sealed class SyncDbContext : DbContext
{
    public SyncDbContext(DbContextOptions<SyncDbContext> options) : base(options) { }

    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Store>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(200);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasIndex(x => new { x.StoreCode, x.Sku }).IsUnique();
            e.Property(x => x.StoreCode).HasMaxLength(64);
            e.Property(x => x.Sku).HasMaxLength(128);
            e.Property(x => x.Cost).HasPrecision(18, 4);
            e.Property(x => x.Price).HasPrecision(18, 4);
            e.Property(x => x.SalePrice).HasPrecision(18, 4);
            e.Property(x => x.Quantity).HasPrecision(18, 4);
            e.Property(x => x.Weight).HasPrecision(18, 4);
        });

        modelBuilder.Entity<ProductImage>(e =>
        {
            e.HasIndex(x => new { x.StoreCode, x.Sku, x.ContentHash }).IsUnique();
            e.Property(x => x.StoreCode).HasMaxLength(64);
            e.Property(x => x.Sku).HasMaxLength(128);
            e.Property(x => x.ContentHash).HasMaxLength(128);
        });

        modelBuilder.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.Endpoint).HasMaxLength(200);
        });

        modelBuilder.Entity<ActivityLog>(e =>
        {
            e.HasIndex(x => x.TimestampUtc);
        });
    }
}
