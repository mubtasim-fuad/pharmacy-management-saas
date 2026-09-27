using Microsoft.EntityFrameworkCore;
using Pharmacy.Api.Data;
using Pharmacy.Api.Models;

var builder = WebApplication.CreateBuilder(args);
var connection = builder.Configuration.GetConnectionString("PharmacyDatabase")
    ?? throw new InvalidOperationException("Set ConnectionStrings__PharmacyDatabase before starting the API.");

builder.Services.AddDbContext<PharmacyDbContext>(options => options.UseNpgsql(connection));
builder.Services.AddCors(options => options.AddPolicy("AngularDevelopment", policy => policy
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();
app.UseCors("AngularDevelopment");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.MapGet("/api/medicines", async (PharmacyDbContext db, CancellationToken token) =>
    await db.Medicines.AsNoTracking().OrderBy(m => m.Name)
        .Select(m => new MedicineResponse(m.Id, m.Name, m.Stock))
        .ToListAsync(token));

app.MapGet("/api/medicines/{id:long}", async (long id, PharmacyDbContext db, CancellationToken token) =>
{
    var medicine = await db.Medicines.AsNoTracking().Where(m => m.Id == id)
        .Select(m => new MedicineResponse(m.Id, m.Name, m.Stock)).SingleOrDefaultAsync(token);
    return medicine is null ? Results.NotFound() : Results.Ok(medicine);
});

app.MapPost("/api/medicines", async (MedicineRequest request, PharmacyDbContext db, CancellationToken token) =>
{
    var name = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || request.Stock < 0)
        return Results.BadRequest(new { error = "Name (1-160 characters) and nonnegative stock are required." });

    var medicine = new Medicine { Name = name, Stock = request.Stock };
    db.Medicines.Add(medicine);
    await db.SaveChangesAsync(token);
    return Results.Created($"/api/medicines/{medicine.Id}", new MedicineResponse(medicine.Id, medicine.Name, medicine.Stock));
});

app.MapPatch("/api/medicines/{id:long}/sell", async (long id, PharmacyDbContext db, CancellationToken token) =>
{
    // A conditional database update prevents concurrent sales from making stock negative.
    var changed = await db.Medicines.Where(m => m.Id == id && m.Stock > 0)
        .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.Stock, m => m.Stock - 1), token);
    if (changed == 0)
        return await db.Medicines.AnyAsync(m => m.Id == id, token)
            ? Results.Conflict(new { error = "Out of stock." })
            : Results.NotFound();

    var updated = await db.Medicines.AsNoTracking().Where(m => m.Id == id)
        .Select(m => new MedicineResponse(m.Id, m.Name, m.Stock)).SingleAsync(token);
    return Results.Ok(updated);
});

app.Run();

record MedicineRequest(string? Name, int Stock);
record MedicineResponse(long Id, string Name, int Stock);
