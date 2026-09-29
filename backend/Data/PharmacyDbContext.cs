using Microsoft.EntityFrameworkCore;
using Pharmacy.Api.Models;

namespace Pharmacy.Api.Data;

public sealed class PharmacyDbContext(DbContextOptions<PharmacyDbContext> options) : DbContext(options)
{
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<Purchase> Purchases => Set<Purchase>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Medicine>(e =>
        {
            e.HasIndex(x => x.Sku).IsUnique().HasFilter("Sku <> ''");
            e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Sku).HasMaxLength(64);
            e.Property(x => x.SalePrice).HasPrecision(12, 2);
            e.Property(x => x.CostPrice).HasPrecision(12, 2);
        });
        model.Entity<Sale>(e =>
        {
            e.Property(x => x.Customer).HasMaxLength(160);
            e.Property(x => x.Total).HasPrecision(12, 2);
            e.Property(x => x.CostTotal).HasPrecision(12, 2);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SaleId);
        });
        model.Entity<SaleItem>(e =>
        {
            e.Property(x => x.MedicineName).HasMaxLength(160);
            e.Property(x => x.UnitPrice).HasPrecision(12, 2);
            e.Property(x => x.UnitCost).HasPrecision(12, 2);
        });
        model.Entity<Purchase>(e =>
        {
            e.Property(x => x.Supplier).HasMaxLength(160);
            e.Property(x => x.Total).HasPrecision(12, 2);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PurchaseId);
        });
        model.Entity<PurchaseItem>(e =>
        {
            e.Property(x => x.MedicineName).HasMaxLength(160);
            e.Property(x => x.UnitCost).HasPrecision(12, 2);
        });
    }
}
