namespace PharmaOrderApp.Models;

public sealed class Order
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string Pharmacy { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public DateTime CreatedAt { get; set; }
}
