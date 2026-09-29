using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pharmacy.Api.Data;
using Pharmacy.Api.Models;

var builder = WebApplication.CreateBuilder(args);
var path = builder.Configuration["DatabasePath"] ?? "pharmacy.db";
var dbPath = Path.GetFullPath(path, builder.Environment.ContentRootPath);
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
builder.Services.AddDbContext<PharmacyDbContext>(options =>
    options.UseSqlite(new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString()));
builder.Services.AddCors(options => options.AddPolicy("LocalWeb", policy => policy
    .WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
    .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<PharmacyDbContext>().Database.EnsureCreatedAsync();
app.UseCors("LocalWeb");
app.MapGet("/health", async (PharmacyDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));

static bool ValidMoney(decimal value) => value >= 0 && value <= 9_999_999_999.99m && decimal.Round(value, 2) == value;
static MedicineResponse ViewMedicine(Medicine m) =>
    new(m.Id, m.Name, m.Sku, m.Stock, m.ReorderLevel, m.SalePrice, m.CostPrice);
static SaleResponse ViewSale(Sale s) => new(s.Id, s.Customer, s.Total,
    DateTime.SpecifyKind(s.CreatedAtUtc, DateTimeKind.Utc),
    s.Items.Select(i => new LineResponse(i.MedicineId, i.MedicineName, i.Quantity, i.UnitPrice)).ToList());
static PurchaseResponse ViewPurchase(Purchase p) => new(p.Id, p.Supplier, p.Total,
    DateTime.SpecifyKind(p.CreatedAtUtc, DateTimeKind.Utc),
    p.Items.Select(i => new LineResponse(i.MedicineId, i.MedicineName, i.Quantity, i.UnitCost)).ToList());

var api = app.MapGroup("/api");
api.MapGet("/medicines", async (PharmacyDbContext db, CancellationToken ct) =>
    (await db.Medicines.AsNoTracking().OrderBy(m => m.Name).ToListAsync(ct)).Select(ViewMedicine));

api.MapPost("/medicines", async Task<IResult> (MedicineRequest request, PharmacyDbContext db, CancellationToken ct) =>
{
    var name = request.Name?.Trim();
    var sku = request.Sku?.Trim().ToUpperInvariant() ?? "";
    if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || sku.Length > 64 ||
        request.ReorderLevel is < 0 or > 1_000_000 || !ValidMoney(request.SalePrice))
        return Results.BadRequest(new { error = "Check the name, SKU, price and reorder level." });
    var medicine = new Medicine { Name = name, Sku = sku,
        ReorderLevel = request.ReorderLevel, SalePrice = request.SalePrice };
    db.Medicines.Add(medicine);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "SKU already exists." }); }
    return Results.Created($"/api/medicines/{medicine.Id}", ViewMedicine(medicine));
});

api.MapPut("/medicines/{id:long}", async Task<IResult> (long id, MedicineRequest request,
    PharmacyDbContext db, CancellationToken ct) =>
{
    var name = request.Name?.Trim();
    var sku = request.Sku?.Trim().ToUpperInvariant() ?? "";
    if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || sku.Length > 64 ||
        request.ReorderLevel is < 0 or > 1_000_000 || !ValidMoney(request.SalePrice))
        return Results.BadRequest(new { error = "Check the name, SKU, price and reorder level." });
    var medicine = await db.Medicines.SingleOrDefaultAsync(m => m.Id == id, ct);
    if (medicine is null) return Results.NotFound();
    medicine.Name = name; medicine.Sku = sku; medicine.SalePrice = request.SalePrice;
    medicine.ReorderLevel = request.ReorderLevel;
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "SKU already exists." }); }
    return Results.Ok(ViewMedicine(medicine));
});

api.MapPost("/purchases", async Task<IResult> (PurchaseRequest request, PharmacyDbContext db, CancellationToken ct) =>
{
    if (request.Items is null || request.Items.Count is < 1 or > 50 ||
        request.Items.GroupBy(i => i.MedicineId).Any(g => g.Count() > 1) ||
        request.Items.Any(i => i.Quantity is < 1 or > 100_000 || !ValidMoney(i.UnitCost)) ||
        (request.Supplier?.Trim().Length ?? 0) > 160 ||
        !ValidMoney(request.Items.Sum(i => i.Quantity * i.UnitCost)))
        return Results.BadRequest(new { error = "Enter valid medicines, quantities and costs." });

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var purchase = new Purchase { Supplier = string.IsNullOrWhiteSpace(request.Supplier)
        ? "Unspecified" : request.Supplier.Trim() };
    foreach (var line in request.Items.OrderBy(i => i.MedicineId))
    {
        var medicine = await db.Medicines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == line.MedicineId, ct);
        if (medicine is null) return Results.BadRequest(new { error = "A medicine is missing from your catalog." });
        var changed = await db.Medicines.Where(m => m.Id == medicine.Id && m.Stock <= int.MaxValue - line.Quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Stock, m => m.Stock + line.Quantity)
                .SetProperty(m => m.CostPrice, line.UnitCost), ct);
        if (changed != 1) return Results.Conflict(new { error = "Stock limit exceeded." });
        purchase.Items.Add(new PurchaseItem { MedicineId = medicine.Id, MedicineName = medicine.Name,
            Quantity = line.Quantity, UnitCost = line.UnitCost });
        purchase.Total += line.Quantity * line.UnitCost;
    }
    db.Purchases.Add(purchase);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Created($"/api/purchases/{purchase.Id}", ViewPurchase(purchase));
});

api.MapPost("/sales", async Task<IResult> (SaleRequest request, PharmacyDbContext db, CancellationToken ct) =>
{
    if (request.Items is null || request.Items.Count is < 1 or > 50 ||
        request.Items.GroupBy(i => i.MedicineId).Any(g => g.Count() > 1) ||
        request.Items.Any(i => i.Quantity is < 1 or > 100_000) ||
        (request.Customer?.Trim().Length ?? 0) > 160)
        return Results.BadRequest(new { error = "Enter valid medicines and quantities." });

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var sale = new Sale { Customer = string.IsNullOrWhiteSpace(request.Customer)
        ? "Walk-in" : request.Customer.Trim() };
    foreach (var line in request.Items.OrderBy(i => i.MedicineId))
    {
        var medicine = await db.Medicines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == line.MedicineId, ct);
        if (medicine is null) return Results.BadRequest(new { error = "A medicine is missing from your catalog." });
        var changed = await db.Medicines.Where(m => m.Id == medicine.Id && m.Stock >= line.Quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Stock, m => m.Stock - line.Quantity), ct);
        if (changed != 1) return Results.Conflict(new { error = $"Insufficient stock for {medicine.Name}. Refresh and try again." });
        sale.Items.Add(new SaleItem { MedicineId = medicine.Id, MedicineName = medicine.Name,
            Quantity = line.Quantity, UnitPrice = medicine.SalePrice, UnitCost = medicine.CostPrice });
        sale.Total += line.Quantity * medicine.SalePrice;
        sale.CostTotal += line.Quantity * medicine.CostPrice;
    }
    if (!ValidMoney(sale.Total) || !ValidMoney(sale.CostTotal))
        return Results.BadRequest(new { error = "Sale total exceeds the supported amount." });
    db.Sales.Add(sale);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Created($"/api/sales/{sale.Id}", ViewSale(sale));
});

api.MapGet("/sales", async (PharmacyDbContext db, CancellationToken ct) =>
    (await db.Sales.AsNoTracking().Include(s => s.Items).OrderByDescending(s => s.CreatedAtUtc)
        .ThenByDescending(s => s.Id).Take(50).ToListAsync(ct)).Select(ViewSale));
api.MapGet("/purchases", async (PharmacyDbContext db, CancellationToken ct) =>
    (await db.Purchases.AsNoTracking().Include(p => p.Items).OrderByDescending(p => p.CreatedAtUtc)
        .ThenByDescending(p => p.Id).Take(50).ToListAsync(ct)).Select(ViewPurchase));

api.MapGet("/reports/summary", async (int? days, PharmacyDbContext db, CancellationToken ct) =>
{
    var period = Math.Clamp(days ?? 30, 1, 90);
    var start = DateTime.UtcNow.Date.AddDays(1 - period);
    var catalog = await db.Medicines.AsNoTracking().ToListAsync(ct);
    // SQLite has no native decimal SUM; aggregate the small local records in .NET.
    var sales = await db.Sales.AsNoTracking().Where(s => s.CreatedAtUtc >= start).ToListAsync(ct);
    var purchases = await db.Purchases.AsNoTracking().Where(p => p.CreatedAtUtc >= start).ToListAsync(ct);
    return new
    {
        days = period,
        medicines = catalog.Count,
        unitsInStock = catalog.Sum(m => (long)m.Stock),
        lowStock = catalog.Count(m => m.Stock <= m.ReorderLevel),
        stockValue = catalog.Sum(m => m.Stock * m.CostPrice),
        salesCount = sales.Count,
        revenue = sales.Sum(s => s.Total),
        costOfGoods = sales.Sum(s => s.CostTotal),
        purchasesTotal = purchases.Sum(p => p.Total)
    };
});

app.Run();

record MedicineRequest(string? Name, string? Sku, decimal SalePrice, int ReorderLevel);
record MedicineResponse(long Id, string Name, string Sku, int Stock, int ReorderLevel, decimal SalePrice, decimal CostPrice);
record SaleRequest(string? Customer, List<SaleLineRequest>? Items);
record SaleLineRequest(long MedicineId, int Quantity);
record PurchaseRequest(string? Supplier, List<PurchaseLineRequest>? Items);
record PurchaseLineRequest(long MedicineId, int Quantity, decimal UnitCost);
record LineResponse(long MedicineId, string MedicineName, int Quantity, decimal UnitPrice);
record SaleResponse(long Id, string Customer, decimal Total, DateTime CreatedAtUtc, List<LineResponse> Items);
record PurchaseResponse(long Id, string Supplier, decimal Total, DateTime CreatedAtUtc, List<LineResponse> Items);
