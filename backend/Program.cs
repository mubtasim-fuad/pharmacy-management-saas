using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Pharmacy.Api.Data;
using Pharmacy.Api.Models;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("PharmacyDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings__PharmacyDatabase.");
var jwtKey = builder.Configuration["JWT:Key"]
    ?? throw new InvalidOperationException("Set JWT__Key to a random secret of at least 32 characters.");
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("JWT__Key must contain at least 32 UTF-8 bytes.");

builder.Services.AddDbContext<PharmacyDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddScoped<IPasswordHasher<PharmacyUser>, PasswordHasher<PharmacyUser>>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = "pharmacy-api",
        ValidateAudience = true,
        ValidAudience = "pharmacy-web",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});
builder.Services.AddAuthorization();
var origins = (builder.Configuration["Cors:AllowedOrigins"] ?? "http://localhost:4200")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddPolicy("Web", policy => policy
    .WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors("Web");
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", async (PharmacyDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "healthy" }) : Results.StatusCode(503));

string CreateToken(PharmacyUser user)
{
    var descriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity([
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("tenant_id", user.TenantId.ToString())
        ]),
        Expires = DateTime.UtcNow.AddHours(8),
        Issuer = "pharmacy-api",
        Audience = "pharmacy-web",
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256)
    };
    return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(descriptor));
}
static long Tenant(HttpContext context) => long.Parse(context.User.FindFirst("tenant_id")!.Value);
static bool ValidMoney(decimal value) => value >= 0 && value <= 9_999_999_999.99m && decimal.Round(value, 2) == value;
static MedicineResponse ViewMedicine(Medicine m) =>
    new(m.Id, m.Name, m.Sku, m.Stock, m.ReorderLevel, m.SalePrice, m.CostPrice);
static SaleResponse ViewSale(Sale s) => new(s.Id, s.Customer, s.Total, s.CreatedAtUtc,
    s.Items.Select(i => new LineResponse(i.MedicineId, i.MedicineName, i.Quantity, i.UnitPrice)).ToList());
static PurchaseResponse ViewPurchase(Purchase p) => new(p.Id, p.Supplier, p.Total, p.CreatedAtUtc,
    p.Items.Select(i => new LineResponse(i.MedicineId, i.MedicineName, i.Quantity, i.UnitCost)).ToList());

app.MapPost("/api/auth/register", async Task<IResult> (RegisterRequest request, PharmacyDbContext db,
    IPasswordHasher<PharmacyUser> hasher, CancellationToken ct) =>
{
    var name = request.PharmacyName?.Trim();
    var email = request.Email?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 160 ||
        string.IsNullOrWhiteSpace(email) || email.Length > 254 || !email.Contains('@') ||
        request.Password is null || request.Password.Length < 12 || request.Password.Length > 128)
        return Results.BadRequest(new { error = "Enter a pharmacy name, valid email and password of 12–128 characters." });
    if (await db.Users.AnyAsync(u => u.Email == email, ct))
        return Results.Conflict(new { error = "That email is already registered." });

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var tenant = new PharmacyTenant { Name = name };
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);
    var user = new PharmacyUser { TenantId = tenant.Id, Email = email, PasswordHash = "" };
    user.PasswordHash = hasher.HashPassword(user, request.Password);
    db.Users.Add(user);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "That email is already registered." }); }
    await tx.CommitAsync(ct);
    return Results.Ok(new AuthResponse(CreateToken(user), tenant.Name, user.Email));
});

app.MapPost("/api/auth/login", async Task<IResult> (LoginRequest request, PharmacyDbContext db,
    IPasswordHasher<PharmacyUser> hasher, CancellationToken ct) =>
{
    var email = request.Email?.Trim().ToLowerInvariant();
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct);
    if (user is null || request.Password is null ||
        hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
        return Results.Unauthorized();
    var pharmacy = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == user.TenantId, ct);
    return Results.Ok(new AuthResponse(CreateToken(user), pharmacy.Name, user.Email));
});

var api = app.MapGroup("/api").RequireAuthorization();
api.MapGet("/medicines", async (HttpContext context, PharmacyDbContext db, CancellationToken ct) =>
    (await db.Medicines.AsNoTracking().Where(m => m.TenantId == Tenant(context) && m.Active)
        .OrderBy(m => m.Name).ToListAsync(ct)).Select(ViewMedicine));

api.MapPost("/medicines", async Task<IResult> (MedicineRequest request, HttpContext context,
    PharmacyDbContext db, CancellationToken ct) =>
{
    var name = request.Name?.Trim();
    var sku = request.Sku?.Trim().ToUpperInvariant() ?? "";
    if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || sku.Length > 64 ||
        request.ReorderLevel < 0 || request.ReorderLevel > 1_000_000 || !ValidMoney(request.SalePrice))
        return Results.BadRequest(new { error = "Check the name, SKU, price and reorder level." });
    var medicine = new Medicine { TenantId = Tenant(context), Name = name, Sku = sku,
        ReorderLevel = request.ReorderLevel, SalePrice = request.SalePrice };
    db.Medicines.Add(medicine);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "SKU already exists in this pharmacy." }); }
    return Results.Created($"/api/medicines/{medicine.Id}", ViewMedicine(medicine));
});

api.MapPut("/medicines/{id:long}", async Task<IResult> (long id, MedicineRequest request, HttpContext context,
    PharmacyDbContext db, CancellationToken ct) =>
{
    var name = request.Name?.Trim();
    var sku = request.Sku?.Trim().ToUpperInvariant() ?? "";
    if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || sku.Length > 64 ||
        request.ReorderLevel < 0 || request.ReorderLevel > 1_000_000 || !ValidMoney(request.SalePrice))
        return Results.BadRequest(new { error = "Check the name, SKU, price and reorder level." });
    var medicine = await db.Medicines.SingleOrDefaultAsync(m => m.Id == id && m.TenantId == Tenant(context) && m.Active, ct);
    if (medicine is null) return Results.NotFound();
    medicine.Name = name; medicine.Sku = sku; medicine.SalePrice = request.SalePrice;
    medicine.ReorderLevel = request.ReorderLevel;
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return Results.Conflict(new { error = "SKU already exists in this pharmacy." }); }
    return Results.Ok(ViewMedicine(medicine));
});

api.MapPost("/purchases", async Task<IResult> (PurchaseRequest request, HttpContext context,
    PharmacyDbContext db, CancellationToken ct) =>
{
    var tenantId = Tenant(context);
    if (request.Items is null || request.Items.Count is < 1 or > 50 ||
        request.Items.GroupBy(i => i.MedicineId).Any(g => g.Count() > 1) ||
        request.Items.Any(i => i.Quantity is < 1 or > 100_000 || !ValidMoney(i.UnitCost)) ||
        (request.Supplier?.Trim().Length ?? 0) > 160)
        return Results.BadRequest(new { error = "Enter 1–50 distinct medicines with valid quantities and costs." });
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var purchase = new Purchase { TenantId = tenantId, Supplier = string.IsNullOrWhiteSpace(request.Supplier)
        ? "Unspecified" : request.Supplier.Trim() };
    foreach (var line in request.Items.OrderBy(i => i.MedicineId))
    {
        var medicine = await db.Medicines.AsNoTracking().SingleOrDefaultAsync(
            m => m.Id == line.MedicineId && m.TenantId == tenantId && m.Active, ct);
        if (medicine is null) return Results.BadRequest(new { error = "A medicine is missing from your catalog." });
        var changed = await db.Medicines.Where(m => m.Id == medicine.Id && m.TenantId == tenantId &&
                m.Stock <= int.MaxValue - line.Quantity)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Stock, m => m.Stock + line.Quantity)
                .SetProperty(m => m.CostPrice, line.UnitCost), ct);
        if (changed != 1) return Results.Conflict(new { error = "Stock limit exceeded." });
        purchase.Items.Add(new PurchaseItem { MedicineId = medicine.Id, MedicineName = medicine.Name,
            Quantity = line.Quantity, UnitCost = line.UnitCost });
        purchase.Total += line.Quantity * line.UnitCost;
    }
    if (!ValidMoney(purchase.Total)) return Results.BadRequest(new { error = "Purchase total exceeds the supported amount." });
    db.Purchases.Add(purchase);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Created($"/api/purchases/{purchase.Id}", ViewPurchase(purchase));
});

api.MapPost("/sales", async Task<IResult> (SaleRequest request, HttpContext context,
    PharmacyDbContext db, CancellationToken ct) =>
{
    var tenantId = Tenant(context);
    if (request.Items is null || request.Items.Count is < 1 or > 50 ||
        request.Items.GroupBy(i => i.MedicineId).Any(g => g.Count() > 1) ||
        request.Items.Any(i => i.Quantity is < 1 or > 100_000) ||
        (request.Customer?.Trim().Length ?? 0) > 160)
        return Results.BadRequest(new { error = "Enter 1–50 distinct medicines with valid quantities." });
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var sale = new Sale { TenantId = tenantId, Customer = string.IsNullOrWhiteSpace(request.Customer)
        ? "Walk-in" : request.Customer.Trim() };
    foreach (var line in request.Items.OrderBy(i => i.MedicineId))
    {
        var medicine = await db.Medicines.AsNoTracking().SingleOrDefaultAsync(
            m => m.Id == line.MedicineId && m.TenantId == tenantId && m.Active, ct);
        if (medicine is null) return Results.BadRequest(new { error = "A medicine is missing from your catalog." });
        var changed = await db.Medicines.Where(m => m.Id == medicine.Id && m.TenantId == tenantId &&
                m.Stock >= line.Quantity)
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

api.MapGet("/sales", async (HttpContext context, PharmacyDbContext db, CancellationToken ct) =>
    (await db.Sales.AsNoTracking().Include(s => s.Items)
        .Where(s => s.TenantId == Tenant(context)).OrderByDescending(s => s.CreatedAtUtc)
        .ThenByDescending(s => s.Id).Take(50).ToListAsync(ct)).Select(ViewSale));
api.MapGet("/purchases", async (HttpContext context, PharmacyDbContext db, CancellationToken ct) =>
    (await db.Purchases.AsNoTracking().Include(p => p.Items)
        .Where(p => p.TenantId == Tenant(context)).OrderByDescending(p => p.CreatedAtUtc)
        .ThenByDescending(p => p.Id).Take(50).ToListAsync(ct)).Select(ViewPurchase));

api.MapGet("/reports/summary", async (int? days, HttpContext context, PharmacyDbContext db, CancellationToken ct) =>
{
    var tenantId = Tenant(context);
    var period = Math.Clamp(days ?? 30, 1, 90);
    var start = DateTime.UtcNow.Date.AddDays(1 - period);
    var catalog = db.Medicines.AsNoTracking().Where(m => m.TenantId == tenantId && m.Active);
    var sales = db.Sales.AsNoTracking().Where(s => s.TenantId == tenantId && s.CreatedAtUtc >= start);
    var purchases = db.Purchases.AsNoTracking().Where(p => p.TenantId == tenantId && p.CreatedAtUtc >= start);
    return new
    {
        days = period,
        medicines = await catalog.CountAsync(ct),
        unitsInStock = await catalog.SumAsync(m => (int?)m.Stock, ct) ?? 0,
        lowStock = await catalog.CountAsync(m => m.Stock <= m.ReorderLevel, ct),
        stockValue = await catalog.SumAsync(m => (decimal?)(m.Stock * m.CostPrice), ct) ?? 0,
        salesCount = await sales.CountAsync(ct),
        revenue = await sales.SumAsync(s => (decimal?)s.Total, ct) ?? 0,
        costOfGoods = await sales.SumAsync(s => (decimal?)s.CostTotal, ct) ?? 0,
        purchasesTotal = await purchases.SumAsync(p => (decimal?)p.Total, ct) ?? 0
    };
});

app.Run();

record RegisterRequest(string? PharmacyName, string? Email, string? Password);
record LoginRequest(string? Email, string? Password);
record AuthResponse(string Token, string PharmacyName, string Email);
record MedicineRequest(string? Name, string? Sku, decimal SalePrice, int ReorderLevel);
record MedicineResponse(long Id, string Name, string Sku, int Stock, int ReorderLevel, decimal SalePrice, decimal CostPrice);
record SaleRequest(string? Customer, List<SaleLineRequest>? Items);
record SaleLineRequest(long MedicineId, int Quantity);
record PurchaseRequest(string? Supplier, List<PurchaseLineRequest>? Items);
record PurchaseLineRequest(long MedicineId, int Quantity, decimal UnitCost);
record LineResponse(long MedicineId, string MedicineName, int Quantity, decimal UnitPrice);
record SaleResponse(long Id, string Customer, decimal Total, DateTime CreatedAtUtc, List<LineResponse> Items);
record PurchaseResponse(long Id, string Supplier, decimal Total, DateTime CreatedAtUtc, List<LineResponse> Items);
