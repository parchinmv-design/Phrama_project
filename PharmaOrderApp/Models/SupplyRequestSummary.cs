namespace PharmaOrderApp.Models;

public sealed class SupplyRequestSummary
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Pharmacy { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string StatusTitle { get; set; } = string.Empty;
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime NeededBy { get; set; }
    public string Comment { get; set; } = string.Empty;
}
