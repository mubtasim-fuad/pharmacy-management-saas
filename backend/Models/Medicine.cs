namespace Pharmacy.Api.Models;

public sealed class PharmacyTenant
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PharmacyUser
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Medicine
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public required string Name { get; set; }
    public string Sku { get; set; } = "";
    public int Stock { get; set; }
    public int ReorderLevel { get; set; } = 5;
    public decimal SalePrice { get; set; }
    public decimal CostPrice { get; set; }
    public bool Active { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class Sale
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public string Customer { get; set; } = "Walk-in";
    public decimal Total { get; set; }
    public decimal CostTotal { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<SaleItem> Items { get; set; } = [];
}

public sealed class SaleItem
{
    public long Id { get; set; }
    public long SaleId { get; set; }
    public long MedicineId { get; set; }
    public required string MedicineName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
}

public sealed class Purchase
{
    public long Id { get; set; }
    public long TenantId { get; set; }
    public string Supplier { get; set; } = "Unspecified";
    public decimal Total { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PurchaseItem> Items { get; set; } = [];
}

public sealed class PurchaseItem
{
    public long Id { get; set; }
    public long PurchaseId { get; set; }
    public long MedicineId { get; set; }
    public required string MedicineName { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
}
