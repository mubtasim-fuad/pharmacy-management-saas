namespace Pharmacy.Api.Models;

public sealed class Medicine
{
    public long Id { get; set; }
    public required string Name { get; set; }
    public int Stock { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
