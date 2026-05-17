using PharmaOrderApp.Models;
using PharmaOrderApp.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PharmaOrderApp;

public partial class MainWindow : Window
{
    private readonly DatabaseService _database = new();
    private readonly ObservableCollection<Product> _products = new();
    private readonly ObservableCollection<CartItem> _cart = new();
    private User _currentUser = new() { FullName = "Гость", Login = "guest", Role = UserRole.Guest };
    private int _editingProductId;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        ProductsGrid.ItemsSource = _products;
        CartList.ItemsSource = _cart;
        Loaded += (_, _) => Start();
    }

    private void Start()
    {
        try
        {
            _database.Initialize();
            LoadFilters();
            _ready = true;
            RefreshProducts();
            Status("База данных создана и заполнена тестовыми данными.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Login_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var user = _database.Authenticate(LoginBox.Text, PasswordBox.Password);
            if (user is null)
            {
                LoginMessage.Text = "Неверный логин или пароль.";
                return;
            }

            _currentUser = user;
            OpenApplication();
        }
        catch (Exception ex)
        {
            LoginMessage.Text = ex.Message;
        }
    }

    private void Guest_Click(object sender, RoutedEventArgs e)
    {
        _currentUser = new User { FullName = "Гость", Login = "guest", Role = UserRole.Guest };
        OpenApplication();
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _cart.Clear();
        UpdateCartTotal();
        LoginPanel.Visibility = Visibility.Visible;
        AppPanel.Visibility = Visibility.Collapsed;
        LoginMessage.Text = string.Empty;
        Status("Вы вышли из системы.");
    }

    private void OpenApplication()
    {
        LoginPanel.Visibility = Visibility.Collapsed;
        AppPanel.Visibility = Visibility.Visible;
        UserLabel.Text = $"{_currentUser.FullName} | роль: {_currentUser.RoleTitle}";
        AdminPanel.Visibility = _currentUser.Role is UserRole.Admin or UserRole.Pharmacist ? Visibility.Visible : Visibility.Collapsed;
        RefreshProducts();
        Status($"Вход выполнен: {_currentUser.RoleTitle}.");
    }

    private void LoadFilters()
    {
        CategoryFilter.Items.Clear();
        CategoryFilter.Items.Add("Все категории");
        foreach (var category in _database.GetCategories()) CategoryFilter.Items.Add(category);
        CategoryFilter.SelectedIndex = 0;

        PharmacyFilter.Items.Clear();
        PharmacyFilter.Items.Add("Все аптеки");
        foreach (var pharmacy in _database.GetPharmacies()) PharmacyFilter.Items.Add(pharmacy);
        PharmacyFilter.SelectedIndex = 0;
    }

    private void Filters_Changed(object sender, EventArgs e)
    {
        if (_ready) RefreshProducts();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadFilters();
        RefreshProducts();
    }

    private void RefreshProducts()
    {
        try
        {
            var sort = (SortBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Название";
            var category = CategoryFilter.SelectedItem?.ToString() ?? "Все категории";
            var pharmacy = PharmacyFilter.SelectedItem?.ToString() ?? "Все аптеки";
            var products = _database.SearchProducts(SearchBox.Text, category, pharmacy, sort);
            _products.Clear();
            foreach (var product in products) _products.Add(product);
            Status($"Загружено товаров: {_products.Count}.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка загрузки списка: {ex.Message}");
        }
    }

    private void ProductsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProductsGrid.SelectedItem is not Product product) return;
        ShowProduct(product);
        FillEditor(product);
    }

    private void ShowProduct(Product product)
    {
        ProductTitle.Text = product.Name;
        ProductInfo.Text = $"Категория: {product.Category}\nАптека: {product.Pharmacy}\nПроизводитель: {product.Manufacturer}\nФорма: {product.Form}\nУсловие отпуска: {product.PrescriptionTitle}\nЦена: {product.PriceTitle}\nОстаток: {product.Stock}\n\n{product.Description}";
    }

    private void AddToCart_Click(object sender, RoutedEventArgs e)
    {
        AddSelectedProductToCart();
    }

    private void AddToCart_Click(object sender, MouseButtonEventArgs e)
    {
        AddSelectedProductToCart();
    }

    private void AddSelectedProductToCart()
    {
        try
        {
            if (ProductsGrid.SelectedItem is not Product product)
            {
                throw new InvalidOperationException("Сначала выберите товар.");
            }

            if (!int.TryParse(QuantityBox.Text, out var quantity) || quantity <= 0)
            {
                throw new InvalidOperationException("Количество должно быть положительным числом.");
            }

            if (quantity > product.Stock)
            {
                throw new InvalidOperationException("Нельзя добавить больше, чем есть на складе.");
            }

            var existing = _cart.FirstOrDefault(x => x.Product.Id == product.Id);
            if (existing is null)
            {
                _cart.Add(new CartItem { Product = product, Quantity = quantity });
            }
            else
            {
                existing.Quantity += quantity;
                CartList.Items.Refresh();
            }

            UpdateCartTotal();
            Status($"Добавлено в корзину: {product.Name}.");
        }
        catch (Exception ex)
        {
            Status(ex.Message);
        }
    }

    private void RemoveCartItem_Click(object sender, RoutedEventArgs e)
    {
        if (CartList.SelectedItem is CartItem item)
        {
            _cart.Remove(item);
            UpdateCartTotal();
            Status("Позиция удалена из корзины.");
        }
    }

    private void CreateOrder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = _database.CreateOrder(_currentUser, _cart);
            _cart.Clear();
            UpdateCartTotal();
            RefreshProducts();
            Status($"Заказ #{id} оформлен. Статус: Новый.");
            MessageBox.Show("Заказ сохранён в базе данных.", "Заказ оформлен", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Status($"Не удалось оформить заказ: {ex.Message}");
        }
    }

    private void NewProduct_Click(object sender, RoutedEventArgs e)
    {
        _editingProductId = 0;
        EditName.Text = string.Empty;
        EditCategory.Text = string.Empty;
        EditPharmacy.Text = string.Empty;
        EditManufacturer.Text = string.Empty;
        EditForm.Text = string.Empty;
        EditPrice.Text = string.Empty;
        EditStock.Text = string.Empty;
        EditPrescription.IsChecked = false;
        EditDescription.Text = string.Empty;
        Status("Форма очищена для добавления нового товара.");
    }

    private void SaveProduct_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureCanManageProducts();
            if (!decimal.TryParse(EditPrice.Text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                throw new InvalidOperationException("Цена заполнена неверно.");
            }
            if (!int.TryParse(EditStock.Text, out var stock))
            {
                throw new InvalidOperationException("Остаток заполнен неверно.");
            }

            var product = new Product
            {
                Id = _editingProductId,
                Name = EditName.Text,
                Category = EditCategory.Text,
                Pharmacy = EditPharmacy.Text,
                Manufacturer = EditManufacturer.Text,
                Form = EditForm.Text,
                Price = price,
                Stock = stock,
                PrescriptionRequired = EditPrescription.IsChecked == true,
                Description = EditDescription.Text
            };
            _database.SaveProduct(product);
            LoadFilters();
            RefreshProducts();
            Status(product.Id == 0 ? "Товар добавлен." : "Товар обновлён.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка сохранения: {ex.Message}");
        }
    }

    private void DeleteProduct_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            EnsureCanManageProducts();
            if (ProductsGrid.SelectedItem is not Product product)
            {
                throw new InvalidOperationException("Выберите товар для удаления.");
            }
            if (MessageBox.Show($"Удалить товар '{product.Name}'?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            _database.DeleteProduct(product.Id);
            RefreshProducts();
            NewProduct_Click(sender, e);
            Status("Товар удалён.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка удаления: {ex.Message}");
        }
    }

    private void FillEditor(Product product)
    {
        _editingProductId = product.Id;
        EditName.Text = product.Name;
        EditCategory.Text = product.Category;
        EditPharmacy.Text = product.Pharmacy;
        EditManufacturer.Text = product.Manufacturer;
        EditForm.Text = product.Form;
        EditPrice.Text = product.Price.ToString(CultureInfo.InvariantCulture);
        EditStock.Text = product.Stock.ToString(CultureInfo.InvariantCulture);
        EditPrescription.IsChecked = product.PrescriptionRequired;
        EditDescription.Text = product.Description;
    }

    private void UpdateCartTotal()
    {
        CartTotal.Text = $"Итого: {_cart.Sum(x => x.Total):N2} ₽";
    }

    private void EnsureCanManageProducts()
    {
        if (_currentUser.Role is not (UserRole.Admin or UserRole.Pharmacist))
        {
            throw new InvalidOperationException("Добавление, редактирование и удаление доступны только админу или фармацевту.");
        }
    }

    private void Status(string message)
    {
        StatusLabel.Text = message;
    }
}
