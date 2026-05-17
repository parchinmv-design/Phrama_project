namespace PharmaOrderApp.Models;

public sealed class CartItem
{
    public Product Product { get; set; } = new();
    public int Quantity { get; set; }
    public decimal Total => Product.Price * Quantity;
    public string Display => $"{Product.Name} x{Quantity} = {Total:N2} ₽";
}
