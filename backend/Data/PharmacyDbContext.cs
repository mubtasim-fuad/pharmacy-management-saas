using Microsoft.EntityFrameworkCore;
using Pharmacy.Api.Models;

namespace Pharmacy.Api.Data;

public sealed class PharmacyDbContext(DbContextOptions<PharmacyDbContext> options) : DbContext(options)
{
    public DbSet<Medicine> Medicines => Set<Medicine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Medicine>(entity =>
        {
            entity.ToTable("medicines", table => table.HasCheckConstraint("ck_medicines_stock_nonnegative", "stock >= 0"));
            entity.HasKey(m => m.Id);
            entity.Property(m => m.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(m => m.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
            entity.Property(m => m.Stock).HasColumnName("stock").IsRequired();
            entity.Property(m => m.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");
        });
    }
}
