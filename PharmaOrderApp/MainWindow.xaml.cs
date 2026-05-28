using PharmaOrderApp.Models;
using PharmaOrderApp.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PharmaOrderApp;

public partial class MainWindow : Window
{
    private readonly DatabaseService _database = new();
    private readonly ObservableCollection<Product> _products = new();
    private readonly ObservableCollection<CartItem> _cart = new();
    private readonly ObservableCollection<Order> _orders = new();
    private readonly ObservableCollection<SupplyRequestSummary> _supplyRequests = new();
    private readonly ObservableCollection<EmployeeSummary> _employees = new();
    private readonly ObservableCollection<ClientSummary> _clients = new();
    private readonly ObservableCollection<ActivityLogEntry> _activityLogs = new();
    private User _currentUser = new() { FullName = "Гость", Login = "guest", Role = UserRole.Guest };
    private int _editingProductId;
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        ProductsGrid.ItemsSource = _products;
        CartList.ItemsSource = _cart;
        OrdersGrid.ItemsSource = _orders;
        SupplyGrid.ItemsSource = _supplyRequests;
        EmployeesGrid.ItemsSource = _employees;
        ClientsGrid.ItemsSource = _clients;
        AuditGrid.ItemsSource = _activityLogs;
        Loaded += (_, _) => Start();
    }

    private void Start()
    {
        try
        {
            _database.Initialize();
            LoadLookups();
            SupplyNeededDatePicker.SelectedDate ??= DateTime.Today.AddDays(3);
            _ready = true;
            RefreshAll();
            Status("Данные синхронизированы. Каталог, роли и окна готовы.");
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

    private void RegisterClient_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _database.RegisterClient(
                RegisterLoginBox.Text,
                RegisterPasswordBox.Password,
                RegisterFullNameBox.Text,
                RegisterPhoneBox.Text,
                RegisterEmailBox.Text);

            RegisterMessage.Text = "Клиент зарегистрирован. Можно входить под новой учётной записью.";
            LoginBox.Text = RegisterLoginBox.Text;
            PasswordBox.Password = RegisterPasswordBox.Password;
            RegisterFullNameBox.Text = string.Empty;
            RegisterLoginBox.Text = string.Empty;
            RegisterPasswordBox.Password = string.Empty;
            RegisterPhoneBox.Text = string.Empty;
            RegisterEmailBox.Text = string.Empty;
            Status("Создан новый клиент.");
        }
        catch (Exception ex)
        {
            RegisterMessage.Text = ex.Message;
        }
    }

    private void Logout_Click(object sender, RoutedEventArgs e)
    {
        _currentUser = new User { FullName = "Гость", Login = "guest", Role = UserRole.Guest };
        _cart.Clear();
        _orders.Clear();
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
        LoginMessage.Text = string.Empty;
        UserLabel.Text = $"{_currentUser.FullName} | роль: {_currentUser.RoleTitle}" +
                         (string.IsNullOrWhiteSpace(_currentUser.AssignedPharmacyName) ? string.Empty : $" | аптека: {_currentUser.AssignedPharmacyName}");

        var isClientArea = _currentUser.Role is UserRole.Client or UserRole.Guest;
        var isManager = _currentUser.Role == UserRole.Manager;
        var isAdmin = _currentUser.Role == UserRole.Admin;

        ClientCatalogTab.Visibility = isClientArea ? Visibility.Visible : Visibility.Collapsed;
        ClientOrdersTab.Visibility = _currentUser.Role == UserRole.Client ? Visibility.Visible : Visibility.Collapsed;
        ManagerTab.Visibility = isManager ? Visibility.Visible : Visibility.Collapsed;
        AdminTab.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        AddToCartButton.IsEnabled = _currentUser.Role == UserRole.Client;
        CreateOrderButton.IsEnabled = _currentUser.Role == UserRole.Client;
        DeliveryMethodBox.IsEnabled = _currentUser.Role == UserRole.Client;
        PaymentMethodBox.IsEnabled = _currentUser.Role == UserRole.Client;
        OrderCommentBox.IsEnabled = _currentUser.Role == UserRole.Client;
        QuantityBox.IsEnabled = _currentUser.Role == UserRole.Client;

        EditPharmacyBox.IsEnabled = isAdmin;
        SupplyPharmacyBox.IsEnabled = isAdmin;
        if (isManager)
        {
            SelectLookupById(EditPharmacyBox, _currentUser.AssignedPharmacyId);
            SelectLookupById(SupplyPharmacyBox, _currentUser.AssignedPharmacyId);
            ManagerPharmacyInfo.Text = $"Закреплённая аптека: {_currentUser.AssignedPharmacyName}";
        }

        RefreshButton.Content = _currentUser.Role switch
        {
            UserRole.Admin => "Обновить контроль",
            UserRole.Manager => "Обновить поставки",
            _ => "Обновить каталог"
        };

        OrdersCaption.Text = _currentUser.Role == UserRole.Client ? "Мои заказы" : "Заказы";

        WorkspaceTabs.SelectedItem = isAdmin ? AdminTab : isManager ? ManagerTab : ClientCatalogTab;
        RefreshAll();
        Status($"Вход выполнен: {_currentUser.RoleTitle}.");
    }

    private void LoadLookups()
    {
        CategoryFilter.ItemsSource = AddAllItem(_database.GetCategories(), "Все категории");
        CategoryFilter.SelectedIndex = 0;
        PharmacyFilter.ItemsSource = AddAllItem(_database.GetPharmacies(), "Все аптеки");
        PharmacyFilter.SelectedIndex = 0;

        EditCategoryBox.ItemsSource = _database.GetCategories();
        EditManufacturerBox.ItemsSource = _database.GetManufacturers();
        EditFormBox.ItemsSource = _database.GetForms();
        EditPharmacyBox.ItemsSource = _database.GetPharmacies();
        SupplySupplierBox.ItemsSource = _database.GetSuppliers();
        SupplyPharmacyBox.ItemsSource = _database.GetPharmacies();
        NewEmployeePharmacyBox.ItemsSource = _database.GetPharmacies();
    }

    private static List<LookupItem> AddAllItem(IReadOnlyList<LookupItem> source, string title)
    {
        var list = new List<LookupItem> { new() { Id = 0, Name = title } };
        list.AddRange(source);
        return list;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadLookups();
        RefreshAll();
    }

    private void RefreshAll()
    {
        RefreshProducts();
        RefreshOrders();
        RefreshManagerData();
        RefreshAdminData();
        SyncProductPickers();
    }

    private void Filters_Changed(object sender, EventArgs e)
    {
        if (_ready)
        {
            RefreshProducts();
        }
    }

    private void RefreshProducts()
    {
        try
        {
            var sort = (SortBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Название";
            var categoryId = (CategoryFilter.SelectedItem as LookupItem)?.Id;
            var pharmacyId = (PharmacyFilter.SelectedItem as LookupItem)?.Id;
            var products = _database.SearchProducts(SearchBox.Text, categoryId > 0 ? categoryId : null, pharmacyId > 0 ? pharmacyId : null, sort);
            _products.Clear();
            foreach (var product in products)
            {
                _products.Add(product);
            }

            BuildCategoryShowcases();
            SyncProductPickers();
            Status($"Каталог обновлён. Найдено препаратов: {_products.Count}.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка загрузки каталога: {ex.Message}");
        }
    }

    private void BuildCategoryShowcases()
    {
        CategoryShowcasePanel.Children.Clear();
        var groups = _products
            .GroupBy(x => x.Category)
            .OrderBy(g => g.Key)
            .Take(6)
            .ToList();

        foreach (var group in groups)
        {
            var card = new Border
            {
                Width = 220,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(255, 255, 255))
            };

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = group.Key,
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Foreground = (Brush)FindResource("InkBrush")
            });

            foreach (var product in group.Take(4))
            {
                var button = new Button
                {
                    Content = $"{product.Name} • {product.PriceTitle}",
                    Tag = product.Id,
                    Background = Brushes.Transparent,
                    Foreground = new SolidColorBrush(Color.FromRgb(54, 67, 75)),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(2, 6, 2, 6),
                    Margin = new Thickness(0, 4, 0, 0)
                };
                button.Click += ShelfProduct_Click;
                stack.Children.Add(button);
            }

            card.Child = stack;
            CategoryShowcasePanel.Children.Add(card);
        }
    }

    private void ShelfProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not int productId)
        {
            return;
        }

        var product = _products.FirstOrDefault(x => x.Id == productId);
        if (product is null)
        {
            return;
        }

        ProductsGrid.SelectedItem = product;
        ProductsGrid.ScrollIntoView(product);
        ShowProduct(product);
    }

    private void RefreshOrders()
    {
        _orders.Clear();
        if (_currentUser.Role != UserRole.Client)
        {
            return;
        }

        foreach (var order in _database.GetOrders(_currentUser))
        {
            _orders.Add(order);
        }
    }

    private void RefreshManagerData()
    {
        _supplyRequests.Clear();
        if (_currentUser.Role != UserRole.Manager)
        {
            return;
        }

        foreach (var item in _database.GetSupplyRequests(_currentUser))
        {
            _supplyRequests.Add(item);
        }
    }

    private void RefreshAdminData()
    {
        if (_currentUser.Role != UserRole.Admin)
        {
            return;
        }

        var summary = _database.GetDashboardSummary(_currentUser);
        KpiClients.Text = summary.TotalClients.ToString(CultureInfo.InvariantCulture);
        KpiOrders.Text = summary.OpenOrders.ToString(CultureInfo.InvariantCulture);
        KpiSupply.Text = summary.OpenSupplyRequests.ToString(CultureInfo.InvariantCulture);

        _employees.Clear();
        foreach (var employee in _database.GetEmployees())
        {
            _employees.Add(employee);
        }

        _clients.Clear();
        foreach (var client in _database.GetClients())
        {
            _clients.Add(client);
        }

        _activityLogs.Clear();
        foreach (var item in _database.GetActivityLogs())
        {
            _activityLogs.Add(item);
        }
    }

    private void SyncProductPickers()
    {
        SupplyProductBox.ItemsSource = _products.ToList();
    }

    private void ProductsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProductsGrid.SelectedItem is not Product product)
        {
            return;
        }

        ShowProduct(product);
        FillEditor(product);
        SupplyProductBox.SelectedItem = _products.FirstOrDefault(x => x.Id == product.Id);
    }

    private void ShowProduct(Product product)
    {
        ProductTitle.Text = product.Name;
        ProductInfo.Text = $"Категория: {product.Category}\n" +
                           $"Аптека: {product.Pharmacy}\n" +
                           $"Производитель: {product.Manufacturer}\n" +
                           $"Форма: {product.Form}\n" +
                           $"Условие отпуска: {product.PrescriptionTitle}\n" +
                           $"Цена: {product.PriceTitle}\n" +
                           $"Остаток: {product.Stock}\n\n{product.Description}";
    }

    private void AddToCart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentUser.Role != UserRole.Client)
            {
                throw new InvalidOperationException("Корзина доступна только зарегистрированному клиенту.");
            }

            if (ProductsGrid.SelectedItem is not Product product)
            {
                throw new InvalidOperationException("Сначала выберите препарат.");
            }

            if (!int.TryParse(QuantityBox.Text, out var quantity) || quantity <= 0)
            {
                throw new InvalidOperationException("Количество должно быть положительным числом.");
            }

            if (quantity > product.Stock)
            {
                throw new InvalidOperationException("Нельзя добавить больше, чем есть в наличии.");
            }

            if (_cart.Any() && _cart.Any(x => x.Product.PharmacyId != product.PharmacyId))
            {
                throw new InvalidOperationException("В корзине могут быть товары только одной аптеки.");
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
            var deliveryMethod = MapDeliveryMethod((DeliveryMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
            var paymentMethod = MapPaymentMethod((PaymentMethodBox.SelectedItem as ComboBoxItem)?.Content?.ToString());
            var id = _database.CreateOrder(_currentUser, _cart, deliveryMethod, paymentMethod, OrderCommentBox.Text);
            _cart.Clear();
            UpdateCartTotal();
            OrderCommentBox.Text = string.Empty;
            RefreshAll();
            WorkspaceTabs.SelectedItem = ClientOrdersTab;
            Status($"Заказ оформлен. Идентификатор: {id}.");
            MessageBox.Show("Заказ сохранён. Смотрите его во вкладке заказов.", "Заказ оформлен", MessageBoxButton.OK, MessageBoxImage.Information);
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
        EditPrice.Text = string.Empty;
        EditStock.Text = "0";
        EditDescription.Text = string.Empty;
        EditPrescription.IsChecked = false;
        EditCategoryBox.SelectedIndex = -1;
        EditManufacturerBox.SelectedIndex = -1;
        EditFormBox.SelectedIndex = -1;
        if (_currentUser.Role == UserRole.Manager)
        {
            SelectLookupById(EditPharmacyBox, _currentUser.AssignedPharmacyId);
        }
        else
        {
            EditPharmacyBox.SelectedIndex = -1;
        }

        Status("Форма очищена для нового товара.");
    }

    private void SaveProduct_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!decimal.TryParse(EditPrice.Text.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                throw new InvalidOperationException("Цена заполнена неверно.");
            }

            if (!int.TryParse(EditStock.Text, out var stock))
            {
                throw new InvalidOperationException("Остаток заполнен неверно.");
            }

            var category = GetSelectedLookup(EditCategoryBox, "категорию");
            var manufacturer = GetSelectedLookup(EditManufacturerBox, "производителя");
            var form = GetSelectedLookup(EditFormBox, "форму выпуска");
            var pharmacy = GetSelectedLookup(EditPharmacyBox, "аптеку");

            var product = new Product
            {
                Id = _editingProductId,
                Name = EditName.Text,
                CategoryId = category.Id,
                Category = category.Name,
                ManufacturerId = manufacturer.Id,
                Manufacturer = manufacturer.Name,
                FormId = form.Id,
                Form = form.Name,
                PharmacyId = pharmacy.Id,
                Pharmacy = pharmacy.Name,
                Price = price,
                Stock = stock,
                PrescriptionRequired = EditPrescription.IsChecked == true,
                Description = EditDescription.Text
            };

            _database.SaveProduct(product, _currentUser);
            LoadLookups();
            RefreshAll();
            Status(product.Id == 0 ? "Новый товар добавлен." : "Товар обновлён.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка сохранения товара: {ex.Message}");
        }
    }

    private void DeleteProduct_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (ProductsGrid.SelectedItem is not Product product)
            {
                throw new InvalidOperationException("Выберите товар, который нужно скрыть.");
            }

            if (MessageBox.Show($"Скрыть товар '{product.Name}' из каталога?", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            _database.DeleteProduct(product.Id, _currentUser);
            RefreshAll();
            NewProduct_Click(sender, e);
            Status("Товар скрыт из каталога.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка изменения товара: {ex.Message}");
        }
    }

    private void FillEditor(Product product)
    {
        _editingProductId = product.Id;
        EditName.Text = product.Name;
        EditPrice.Text = product.Price.ToString(CultureInfo.InvariantCulture);
        EditStock.Text = product.Stock.ToString(CultureInfo.InvariantCulture);
        EditDescription.Text = product.Description;
        EditPrescription.IsChecked = product.PrescriptionRequired;
        SelectLookupById(EditCategoryBox, product.CategoryId);
        SelectLookupById(EditManufacturerBox, product.ManufacturerId);
        SelectLookupById(EditFormBox, product.FormId);
        SelectLookupById(EditPharmacyBox, product.PharmacyId);
    }

    private void CreateSupplyRequest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var product = SupplyProductBox.SelectedItem as Product ?? throw new InvalidOperationException("Выберите товар для заявки.");
            var supplier = GetSelectedLookup(SupplySupplierBox, "поставщика");
            var pharmacy = GetSelectedLookup(SupplyPharmacyBox, "аптеку");
            if (!int.TryParse(SupplyQuantityBox.Text, out var quantity) || quantity <= 0)
            {
                throw new InvalidOperationException("Количество в заявке должно быть больше нуля.");
            }

            var priority = (SupplyPriorityBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Средний";
            var neededBy = SupplyNeededDatePicker.SelectedDate ?? DateTime.Today.AddDays(3);
            _database.CreateSupplyRequest(_currentUser, product.Id, supplier.Id, pharmacy.Id, quantity, priority, neededBy, SupplyCommentBox.Text);
            SupplyCommentBox.Text = string.Empty;
            SupplyQuantityBox.Text = "10";
            RefreshManagerData();
            RefreshAdminData();
            Status("Заявка на поставку создана.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка создания поставки: {ex.Message}");
        }
    }

    private void CreateEmployee_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var roleName = (NewEmployeeRoleBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Manager";
            var role = roleName == "Admin" ? UserRole.Admin : UserRole.Manager;
            int? pharmacyId = null;
            if (role == UserRole.Manager)
            {
                pharmacyId = GetSelectedLookup(NewEmployeePharmacyBox, "аптеку для менеджера").Id;
            }

            _database.CreateEmployee(
                _currentUser,
                role,
                NewEmployeeLoginBox.Text,
                NewEmployeePasswordBox.Password,
                NewEmployeeNameBox.Text,
                NewEmployeePhoneBox.Text,
                NewEmployeeEmailBox.Text,
                pharmacyId);

            NewEmployeeNameBox.Text = string.Empty;
            NewEmployeeLoginBox.Text = string.Empty;
            NewEmployeePasswordBox.Password = string.Empty;
            NewEmployeePhoneBox.Text = string.Empty;
            NewEmployeeEmailBox.Text = string.Empty;
            NewEmployeePharmacyBox.SelectedIndex = -1;
            RefreshAdminData();
            Status("Новый сотрудник создан.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка создания сотрудника: {ex.Message}");
        }
    }

    private void ActivateEmployee_Click(object sender, RoutedEventArgs e) => ChangeEmployeeStatus("Active");

    private void DismissEmployee_Click(object sender, RoutedEventArgs e) => ChangeEmployeeStatus("Dismissed");

    private void BlockEmployee_Click(object sender, RoutedEventArgs e) => ChangeEmployeeStatus("Blocked");

    private void ChangeEmployeeStatus(string statusName)
    {
        try
        {
            if (EmployeesGrid.SelectedItem is not EmployeeSummary employee)
            {
                throw new InvalidOperationException("Выберите сотрудника в таблице.");
            }

            _database.ChangeEmployeeStatus(_currentUser, employee.Id, statusName);
            RefreshAdminData();
            Status($"Статус сотрудника обновлён: {statusName}.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка изменения статуса: {ex.Message}");
        }
    }

    private void EmployeesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmployeesGrid.SelectedItem is not EmployeeSummary employee)
            return;

        EditEmployeeNameBox.Text = employee.FullName;
        EditEmployeePhoneBox.Text = employee.Phone;
        EditEmployeeEmailBox.Text = employee.Email;
        EditEmployeePositionBox.Text = employee.PositionTitle;
        EditEmployeePersonnelBox.Text = employee.PersonnelNumber;
        EditEmployeeSalaryBox.Text = employee.Salary.ToString();
    }

    private void UpdateEmployee_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (EmployeesGrid.SelectedItem is not EmployeeSummary employee)
            {
                throw new InvalidOperationException("Выберите сотрудника в таблице.");
            }

            if (!decimal.TryParse(EditEmployeeSalaryBox.Text, out var salary) || salary < 0)
            {
                throw new InvalidOperationException("Зарплата должна быть неотрицательным числом.");
            }

            _database.UpdateEmployee(
                _currentUser,
                employee.Id,
                EditEmployeeNameBox.Text,
                EditEmployeePhoneBox.Text,
                EditEmployeeEmailBox.Text,
                EditEmployeePositionBox.Text,
                salary,
                null);

            RefreshAdminData();
            Status($"Данные сотрудника {employee.FullName} обновлены.");
        }
        catch (Exception ex)
        {
            Status($"Ошибка обновления сотрудника: {ex.Message}");
        }
    }

    private static LookupItem GetSelectedLookup(ComboBox comboBox, string caption)
    {
        if (comboBox.SelectedItem is not LookupItem item || item.Id <= 0)
        {
            throw new InvalidOperationException($"Выберите {caption}.");
        }

        return item;
    }

    private static void SelectLookupById(ComboBox comboBox, int id)
    {
        foreach (var item in comboBox.Items)
        {
            if (item is LookupItem lookup && lookup.Id == id)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void UpdateCartTotal()
    {
        CartTotal.Text = $"Итого: {_cart.Sum(x => x.Total):N2} ₽";
    }

    private static string MapDeliveryMethod(string? value) => value switch
    {
        "Курьер" => "Courier",
        _ => "Pickup"
    };

    private static string MapPaymentMethod(string? value) => value switch
    {
        "Карта" => "Card",
        "Наличные" => "Cash",
        _ => "CashOnDelivery"
    };

    private void Status(string message)
    {
        StatusLabel.Text = message;
    }
}
