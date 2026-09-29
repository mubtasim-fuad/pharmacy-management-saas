using Microsoft.EntityFrameworkCore;
using Pharmacy.Api.Models;

namespace Pharmacy.Api.Data;

public sealed class PharmacyDbContext(DbContextOptions<PharmacyDbContext> options) : DbContext(options)
{
    public DbSet<PharmacyTenant> Tenants => Set<PharmacyTenant>();
    public DbSet<PharmacyUser> Users => Set<PharmacyUser>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<PharmacyTenant>(e =>
        {
            e.ToTable("tenants"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(160);
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        });
        model.Entity<PharmacyUser>(e =>
        {
            e.ToTable("users"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Email).HasColumnName("email").HasMaxLength(254);
            e.Property(x => x.PasswordHash).HasColumnName("password_hash");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        });
        model.Entity<Medicine>(e =>
        {
            e.ToTable("medicines"); e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique().HasFilter("sku <> ''");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(160);
            e.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(64);
            e.Property(x => x.Stock).HasColumnName("stock");
            e.Property(x => x.ReorderLevel).HasColumnName("reorder_level");
            e.Property(x => x.SalePrice).HasColumnName("sale_price").HasPrecision(12, 2);
            e.Property(x => x.CostPrice).HasColumnName("cost_price").HasPrecision(12, 2);
            e.Property(x => x.Active).HasColumnName("active");
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        });
        model.Entity<Sale>(e =>
        {
            e.ToTable("sales"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Customer).HasColumnName("customer").HasMaxLength(160);
            e.Property(x => x.Total).HasColumnName("total").HasPrecision(12, 2);
            e.Property(x => x.CostTotal).HasColumnName("cost_total").HasPrecision(12, 2);
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SaleId);
        });
        model.Entity<SaleItem>(e =>
        {
            e.ToTable("sale_items"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SaleId).HasColumnName("sale_id");
            e.Property(x => x.MedicineId).HasColumnName("medicine_id");
            e.Property(x => x.MedicineName).HasColumnName("medicine_name").HasMaxLength(160);
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 2);
            e.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(12, 2);
        });
        model.Entity<Purchase>(e =>
        {
            e.ToTable("purchases"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.Supplier).HasColumnName("supplier").HasMaxLength(160);
            e.Property(x => x.Total).HasColumnName("total").HasPrecision(12, 2);
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PurchaseId);
        });
        model.Entity<PurchaseItem>(e =>
        {
            e.ToTable("purchase_items"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.PurchaseId).HasColumnName("purchase_id");
            e.Property(x => x.MedicineId).HasColumnName("medicine_id");
            e.Property(x => x.MedicineName).HasColumnName("medicine_name").HasMaxLength(160);
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.UnitCost).HasColumnName("unit_cost").HasPrecision(12, 2);
        });
    }
}
