namespace PharmaOrderApp.Models;

public sealed class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Pharmacy { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public bool PrescriptionRequired { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string Description { get; set; } = string.Empty;

    public string PrescriptionTitle => PrescriptionRequired ? "По рецепту" : "Без рецепта";
    public string PriceTitle => $"{Price:N2} ₽";
}
