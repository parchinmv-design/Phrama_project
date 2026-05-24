using Microsoft.Data.Sqlite;
using PharmaOrderApp.Models;
using System.IO;

namespace PharmaOrderApp.Services;

public sealed class DatabaseService
{
    private const string SchemaVersion = "4";
    private readonly string _connectionString;
    private readonly string _dbPath;

    public DatabaseService()
    {
        var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaOrderApp");
        Directory.CreateDirectory(appFolder);
        _dbPath = Path.Combine(appFolder, "pharma.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        if (!IsCurrentSchema(connection))
        {
            RebuildDatabase(connection);
        }
    }

    public User? Authenticate(string login, string password)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id,
                   u.login,
                   u.password,
                   u.full_name,
                   u.phone,
                   u.email,
                   r.name AS role_name,
                   coalesce(u.assigned_pharmacy_id, 0),
                   coalesce(ph.name, ''),
                   us.name AS status_name
            FROM users u
            JOIN roles r ON r.id = u.role_id
            JOIN user_statuses us ON us.id = u.status_id
            LEFT JOIN pharmacies ph ON ph.id = u.assigned_pharmacy_id
            WHERE lower(u.login) = lower($login) AND u.password = $password;
            """;
        command.Parameters.AddWithValue("$login", login.Trim());
        command.Parameters.AddWithValue("$password", password);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var user = ReadUser(reader);
        if (!user.IsActive)
        {
            throw new InvalidOperationException("Учётная запись неактивна. Обратитесь к администратору.");
        }

        return user;
    }

    public void RegisterClient(string login, string password, string fullName, string phone, string email)
    {
        if (string.IsNullOrWhiteSpace(login) || login.Trim().Length < 4)
        {
            throw new InvalidOperationException("Логин должен содержать минимум 4 символа.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
        {
            throw new InvalidOperationException("Пароль должен содержать минимум 4 символа.");
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new InvalidOperationException("ФИО клиента обязательно.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        EnsureLoginAvailable(connection, transaction, login);

        var userId = InsertUser(
            connection,
            transaction,
            roleName: "Client",
            statusName: "Active",
            login: login,
            password: password,
            fullName: fullName,
            phone: phone,
            email: email,
            assignedPharmacyId: null,
            createdBy: null);

        using var clientProfileCommand = connection.CreateCommand();
        clientProfileCommand.Transaction = transaction;
        clientProfileCommand.CommandText = """
            INSERT INTO client_profiles(user_id, birth_date, address, bonus_level)
            VALUES($userId, null, '', 'Silver');
            INSERT INTO loyalty_accounts(user_id, points, tier, updated_at)
            VALUES($userId, 0, 'Silver', datetime('now'));
            """;
        clientProfileCommand.Parameters.AddWithValue("$userId", userId);
        clientProfileCommand.ExecuteNonQuery();

        LogAudit(connection, transaction, userId, "register_client", "users", userId, $"Самостоятельная регистрация клиента {fullName}.");
        transaction.Commit();
    }

    public IReadOnlyList<LookupItem> GetCategories() => GetLookupItems("""
        SELECT id, name
        FROM categories
        ORDER BY name;
        """);

    public IReadOnlyList<LookupItem> GetPharmacies() => GetLookupItems("""
        SELECT id, name
        FROM pharmacies
        ORDER BY name;
        """);

    public IReadOnlyList<LookupItem> GetManufacturers() => GetLookupItems("""
        SELECT id, name
        FROM manufacturers
        ORDER BY name;
        """);

    public IReadOnlyList<LookupItem> GetForms() => GetLookupItems("""
        SELECT id, name
        FROM product_forms
        ORDER BY name;
        """);

    public IReadOnlyList<LookupItem> GetSuppliers() => GetLookupItems("""
        SELECT id, name
        FROM suppliers
        ORDER BY name;
        """);

    public List<Product> SearchProducts(string search, int? categoryId, int? pharmacyId, string sort)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var orderBy = sort switch
        {
            "Цена ↑" => "p.base_price ASC",
            "Цена ↓" => "p.base_price DESC",
            "Остаток ↓" => "stock DESC",
            _ => "p.name ASC"
        };

        command.CommandText = $"""
            SELECT p.id,
                   p.category_id,
                   ph.id AS pharmacy_id,
                   p.manufacturer_id,
                   p.form_id,
                   p.name,
                   c.name AS category_name,
                   ph.name AS pharmacy_name,
                   m.name AS manufacturer_name,
                   f.name AS form_name,
                   p.prescription_required,
                   p.base_price,
                   coalesce(sum(ib.quantity - ib.reserved_quantity), 0) AS stock,
                   p.description
            FROM products p
            JOIN categories c ON c.id = p.category_id
            JOIN manufacturers m ON m.id = p.manufacturer_id
            JOIN product_forms f ON f.id = p.form_id
            JOIN inventory_balances ib ON ib.product_id = p.id
            JOIN pharmacies ph ON ph.id = ib.pharmacy_id
            WHERE p.is_active = 1
              AND ($search = '' OR lower(p.name) LIKE '%' || lower($search) || '%' OR lower(m.name) LIKE '%' || lower($search) || '%')
              AND ($categoryId IS NULL OR p.category_id = $categoryId)
              AND ($pharmacyId IS NULL OR ph.id = $pharmacyId)
            GROUP BY p.id, c.name, ph.id, ph.name, m.name, f.name
            HAVING stock > 0
            ORDER BY {orderBy};
            """;
        command.Parameters.AddWithValue("$search", search.Trim());
        command.Parameters.AddWithValue("$categoryId", categoryId is null ? DBNull.Value : categoryId.Value);
        command.Parameters.AddWithValue("$pharmacyId", pharmacyId is null ? DBNull.Value : pharmacyId.Value);

        var result = new List<Product>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadProduct(reader));
        }

        return result;
    }

    public void SaveProduct(Product product, User actor)
    {
        EnsureCanManageProducts(actor);
        ValidateProduct(product);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = product.Id == 0
            ? """
              INSERT INTO products(category_id, form_id, manufacturer_id, sku, name, prescription_required, base_price, description, is_active)
              VALUES($categoryId, $formId, $manufacturerId, $sku, $name, $prescriptionRequired, $price, $description, 1);
              SELECT last_insert_rowid();
              """
            : """
              UPDATE products
              SET category_id = $categoryId,
                  form_id = $formId,
                  manufacturer_id = $manufacturerId,
                  name = $name,
                  prescription_required = $prescriptionRequired,
                  base_price = $price,
                  description = $description
              WHERE id = $id;
              SELECT $id;
              """;
        command.Parameters.AddWithValue("$id", product.Id);
        command.Parameters.AddWithValue("$categoryId", product.CategoryId);
        command.Parameters.AddWithValue("$formId", product.FormId);
        command.Parameters.AddWithValue("$manufacturerId", product.ManufacturerId);
        command.Parameters.AddWithValue("$sku", $"SKU-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}");
        command.Parameters.AddWithValue("$name", product.Name.Trim());
        command.Parameters.AddWithValue("$prescriptionRequired", product.PrescriptionRequired ? 1 : 0);
        command.Parameters.AddWithValue("$price", product.Price);
        command.Parameters.AddWithValue("$description", product.Description.Trim());
        var productId = Convert.ToInt32((long)command.ExecuteScalar()!);

        var targetPharmacyId = actor.Role == UserRole.Manager ? actor.AssignedPharmacyId : product.PharmacyId;
        EnsureInventoryRow(connection, transaction, productId, targetPharmacyId, product.Stock);
        LogAudit(connection, transaction, actor.Id, product.Id == 0 ? "create_product" : "update_product", "products", productId, $"Товар {product.Name} сохранён.");
        transaction.Commit();
    }

    public void DeleteProduct(int productId, User actor)
    {
        EnsureCanManageProducts(actor);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        using var deactivate = connection.CreateCommand();
        deactivate.Transaction = transaction;
        deactivate.CommandText = "UPDATE products SET is_active = 0 WHERE id = $id;";
        deactivate.Parameters.AddWithValue("$id", productId);
        deactivate.ExecuteNonQuery();
        LogAudit(connection, transaction, actor.Id, "archive_product", "products", productId, "Товар скрыт из активного каталога.");
        transaction.Commit();
    }

    public int CreateOrder(User user, IEnumerable<CartItem> cart, string deliveryMethod, string paymentMethod, string comment)
    {
        if (user.Role == UserRole.Guest)
        {
            throw new InvalidOperationException("Оформление заказа доступно только зарегистрированным клиентам.");
        }

        var items = cart.Where(x => x.Quantity > 0).ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Корзина пуста.");
        }

        var pharmacyId = items[0].Product.PharmacyId;
        if (items.Any(x => x.Product.PharmacyId != pharmacyId))
        {
            throw new InvalidOperationException("В одном заказе должны быть товары только из одной аптеки.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        foreach (var item in items)
        {
            var fresh = GetProductStock(connection, transaction, item.Product.Id, item.Product.PharmacyId);
            if (fresh < item.Quantity)
            {
                throw new InvalidOperationException($"Недостаточно товара: {item.Product.Name}. Остаток: {fresh}.");
            }
        }

        var total = items.Sum(x => x.Total);
        var number = $"ORD-{DateTime.Now:yyyyMMdd-HHmmss}";
        var orderStatusId = GetLookupId(connection, transaction, "order_statuses", "New");
        var paymentMethodId = GetLookupId(connection, transaction, "payment_methods", paymentMethod);

        using var orderCommand = connection.CreateCommand();
        orderCommand.Transaction = transaction;
        orderCommand.CommandText = """
            INSERT INTO orders(number, user_id, pharmacy_id, status_id, total, created_at, delivery_method, payment_method_id, comment)
            VALUES($number, $userId, $pharmacyId, $statusId, $total, datetime('now'), $deliveryMethod, $paymentMethodId, $comment);
            SELECT last_insert_rowid();
            """;
        orderCommand.Parameters.AddWithValue("$number", number);
        orderCommand.Parameters.AddWithValue("$userId", user.Id);
        orderCommand.Parameters.AddWithValue("$pharmacyId", pharmacyId);
        orderCommand.Parameters.AddWithValue("$statusId", orderStatusId);
        orderCommand.Parameters.AddWithValue("$total", total);
        orderCommand.Parameters.AddWithValue("$deliveryMethod", deliveryMethod);
        orderCommand.Parameters.AddWithValue("$paymentMethodId", paymentMethodId);
        orderCommand.Parameters.AddWithValue("$comment", comment.Trim());
        var orderId = Convert.ToInt32((long)orderCommand.ExecuteScalar()!);

        foreach (var item in items)
        {
            using var itemCommand = connection.CreateCommand();
            itemCommand.Transaction = transaction;
            itemCommand.CommandText = """
                INSERT INTO order_items(order_id, product_id, quantity, price)
                VALUES($orderId, $productId, $quantity, $price);
                UPDATE inventory_balances
                SET quantity = quantity - $quantity
                WHERE product_id = $productId AND pharmacy_id = $pharmacyId;
                INSERT INTO inventory_movements(product_id, pharmacy_id, batch_id, movement_type_id, quantity, occurred_at, performed_by_user_id, comment)
                VALUES($productId, $pharmacyId, null,
                       (SELECT id FROM movement_types WHERE name = 'Sale'),
                       $quantity, datetime('now'), $userId, 'Списание при продаже');
                """;
            itemCommand.Parameters.AddWithValue("$orderId", orderId);
            itemCommand.Parameters.AddWithValue("$productId", item.Product.Id);
            itemCommand.Parameters.AddWithValue("$quantity", item.Quantity);
            itemCommand.Parameters.AddWithValue("$price", item.Product.Price);
            itemCommand.Parameters.AddWithValue("$pharmacyId", item.Product.PharmacyId);
            itemCommand.Parameters.AddWithValue("$userId", user.Id);
            itemCommand.ExecuteNonQuery();
        }

        using var historyCommand = connection.CreateCommand();
        historyCommand.Transaction = transaction;
        historyCommand.CommandText = """
            INSERT INTO order_status_history(order_id, status_id, changed_at, changed_by_user_id, comment)
            VALUES($orderId, $statusId, datetime('now'), $userId, 'Заказ создан клиентом.');
            INSERT INTO payments(order_id, payment_method_id, amount, status, paid_at)
            VALUES($orderId, $paymentMethodId, $total, 'pending', null);
            """;
        historyCommand.Parameters.AddWithValue("$orderId", orderId);
        historyCommand.Parameters.AddWithValue("$statusId", orderStatusId);
        historyCommand.Parameters.AddWithValue("$userId", user.Id);
        historyCommand.Parameters.AddWithValue("$paymentMethodId", paymentMethodId);
        historyCommand.Parameters.AddWithValue("$total", total);
        historyCommand.ExecuteNonQuery();

        if (string.Equals(deliveryMethod, "Courier", StringComparison.OrdinalIgnoreCase))
        {
            using var deliveryCommand = connection.CreateCommand();
            deliveryCommand.Transaction = transaction;
            deliveryCommand.CommandText = """
                INSERT INTO deliveries(order_id, status_id, address, planned_at, delivered_at, courier_name)
                VALUES($orderId, (SELECT id FROM delivery_statuses WHERE name = 'Planned'), 'Адрес клиента уточняется', datetime('now', '+1 day'), null, 'Назначается');
                """;
            deliveryCommand.Parameters.AddWithValue("$orderId", orderId);
            deliveryCommand.ExecuteNonQuery();
        }

        AddLoyaltyPoints(connection, transaction, user.Id, total);
        LogAudit(connection, transaction, user.Id, "create_order", "orders", orderId, $"Создан заказ {number} на сумму {total:N2}.");
        transaction.Commit();

        return orderId;
    }

    public List<Order> GetOrders(User user)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT o.id,
                   o.number,
                   coalesce(u.full_name, 'Клиент') AS client_name,
                   ph.name AS pharmacy_name,
                   os.title AS status_title,
                   o.delivery_method,
                   o.total,
                   o.created_at
            FROM orders o
            LEFT JOIN users u ON u.id = o.user_id
            JOIN pharmacies ph ON ph.id = o.pharmacy_id
            JOIN order_statuses os ON os.id = o.status_id
            WHERE ($isAdmin = 1)
               OR ($isManager = 1 AND o.pharmacy_id = $pharmacyId)
               OR ($isClient = 1 AND o.user_id = $userId)
            ORDER BY o.created_at DESC;
            """;
        command.Parameters.AddWithValue("$isAdmin", user.Role == UserRole.Admin ? 1 : 0);
        command.Parameters.AddWithValue("$isManager", user.Role == UserRole.Manager ? 1 : 0);
        command.Parameters.AddWithValue("$isClient", user.Role == UserRole.Client ? 1 : 0);
        command.Parameters.AddWithValue("$pharmacyId", user.AssignedPharmacyId);
        command.Parameters.AddWithValue("$userId", user.Id);

        var result = new List<Order>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new Order
            {
                Id = reader.GetInt32(0),
                Number = reader.GetString(1),
                ClientName = reader.GetString(2),
                Pharmacy = reader.GetString(3),
                Status = reader.GetString(4),
                DeliveryMethod = reader.GetString(5),
                Total = reader.GetDecimal(6),
                CreatedAt = DateTime.Parse(reader.GetString(7))
            });
        }

        return result;
    }

    public DashboardSummary GetDashboardSummary(User user)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM users u JOIN roles r ON r.id = u.role_id WHERE r.name = 'Client') AS total_clients,
                (SELECT COUNT(*) FROM users u JOIN roles r ON r.id = u.role_id JOIN user_statuses us ON us.id = u.status_id WHERE r.name = 'Manager' AND us.name = 'Active') AS active_managers,
                (SELECT COUNT(*) FROM users u JOIN roles r ON r.id = u.role_id JOIN user_statuses us ON us.id = u.status_id WHERE r.name = 'Admin' AND us.name = 'Active') AS active_admins,
                (SELECT COUNT(*) FROM orders o JOIN order_statuses os ON os.id = o.status_id
                    WHERE os.name IN ('New', 'Processing')
                      AND ($isAdmin = 1 OR o.pharmacy_id = $pharmacyId)) AS open_orders,
                (SELECT COUNT(*) FROM supply_requests sr JOIN supply_statuses ss ON ss.id = sr.status_id
                    WHERE ss.name IN ('Open', 'Approved', 'InTransit')
                      AND ($isAdmin = 1 OR sr.pharmacy_id = $pharmacyId)) AS open_supply_requests,
                (SELECT coalesce(SUM(total), 0) FROM orders WHERE ($isAdmin = 1 OR pharmacy_id = $pharmacyId)) AS revenue;
            """;
        command.Parameters.AddWithValue("$isAdmin", user.Role == UserRole.Admin ? 1 : 0);
        command.Parameters.AddWithValue("$pharmacyId", user.AssignedPharmacyId);
        using var reader = command.ExecuteReader();
        reader.Read();
        return new DashboardSummary
        {
            TotalClients = reader.GetInt32(0),
            ActiveManagers = reader.GetInt32(1),
            ActiveAdmins = reader.GetInt32(2),
            OpenOrders = reader.GetInt32(3),
            OpenSupplyRequests = reader.GetInt32(4),
            Revenue = reader.GetDecimal(5)
        };
    }

    public List<SupplyRequestSummary> GetSupplyRequests(User user)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sr.id,
                   sr.number,
                   ph.name,
                   s.name,
                   p.name,
                   sr.quantity,
                   sr.priority,
                   ss.title,
                   u.full_name,
                   sr.needed_by,
                   sr.comment
            FROM supply_requests sr
            JOIN pharmacies ph ON ph.id = sr.pharmacy_id
            JOIN suppliers s ON s.id = sr.supplier_id
            JOIN products p ON p.id = sr.product_id
            JOIN supply_statuses ss ON ss.id = sr.status_id
            JOIN users u ON u.id = sr.requested_by_user_id
            WHERE $isAdmin = 1 OR sr.pharmacy_id = $pharmacyId
            ORDER BY sr.created_at DESC;
            """;
        command.Parameters.AddWithValue("$isAdmin", user.Role == UserRole.Admin ? 1 : 0);
        command.Parameters.AddWithValue("$pharmacyId", user.AssignedPharmacyId);

        var result = new List<SupplyRequestSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new SupplyRequestSummary
            {
                Id = reader.GetInt32(0),
                Number = reader.GetString(1),
                Pharmacy = reader.GetString(2),
                Supplier = reader.GetString(3),
                Product = reader.GetString(4),
                Quantity = reader.GetInt32(5),
                Priority = reader.GetString(6),
                StatusTitle = reader.GetString(7),
                RequestedBy = reader.GetString(8),
                NeededBy = DateTime.Parse(reader.GetString(9)),
                Comment = reader.GetString(10)
            });
        }

        return result;
    }

    public void CreateSupplyRequest(User user, int productId, int supplierId, int pharmacyId, int quantity, string priority, DateTime neededBy, string comment)
    {
        if (user.Role is not (UserRole.Manager or UserRole.Admin))
        {
            throw new InvalidOperationException("Заявки на поставку создают менеджеры и администраторы.");
        }

        if (quantity <= 0)
        {
            throw new InvalidOperationException("Количество в поставке должно быть положительным.");
        }

        var targetPharmacyId = user.Role == UserRole.Manager ? user.AssignedPharmacyId : pharmacyId;
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var number = $"SUP-{DateTime.Now:yyyyMMdd-HHmmss}";
        var purchaseNumber = $"PO-{DateTime.Now:yyyyMMdd-HHmmss}";
        var supplyStatusId = GetLookupId(connection, transaction, "supply_statuses", "Open");

        using var requestCommand = connection.CreateCommand();
        requestCommand.Transaction = transaction;
        requestCommand.CommandText = """
            INSERT INTO supply_requests(number, product_id, supplier_id, pharmacy_id, requested_by_user_id, quantity, status_id, priority, needed_by, created_at, comment)
            VALUES($number, $productId, $supplierId, $pharmacyId, $userId, $quantity, $statusId, $priority, $neededBy, datetime('now'), $comment);
            SELECT last_insert_rowid();
            """;
        requestCommand.Parameters.AddWithValue("$number", number);
        requestCommand.Parameters.AddWithValue("$productId", productId);
        requestCommand.Parameters.AddWithValue("$supplierId", supplierId);
        requestCommand.Parameters.AddWithValue("$pharmacyId", targetPharmacyId);
        requestCommand.Parameters.AddWithValue("$userId", user.Id);
        requestCommand.Parameters.AddWithValue("$quantity", quantity);
        requestCommand.Parameters.AddWithValue("$statusId", supplyStatusId);
        requestCommand.Parameters.AddWithValue("$priority", priority);
        requestCommand.Parameters.AddWithValue("$neededBy", neededBy.ToString("yyyy-MM-dd"));
        requestCommand.Parameters.AddWithValue("$comment", comment.Trim());
        var supplyRequestId = Convert.ToInt32((long)requestCommand.ExecuteScalar()!);

        using var purchaseOrderCommand = connection.CreateCommand();
        purchaseOrderCommand.Transaction = transaction;
        purchaseOrderCommand.CommandText = """
            INSERT INTO purchase_orders(number, supplier_id, pharmacy_id, manager_user_id, status_id, planned_delivery, total_cost, created_at)
            VALUES($number, $supplierId, $pharmacyId, $userId, $statusId, $plannedDelivery, $totalCost, datetime('now'));
            SELECT last_insert_rowid();
            """;
        purchaseOrderCommand.Parameters.AddWithValue("$number", purchaseNumber);
        purchaseOrderCommand.Parameters.AddWithValue("$supplierId", supplierId);
        purchaseOrderCommand.Parameters.AddWithValue("$pharmacyId", targetPharmacyId);
        purchaseOrderCommand.Parameters.AddWithValue("$userId", user.Id);
        purchaseOrderCommand.Parameters.AddWithValue("$statusId", supplyStatusId);
        purchaseOrderCommand.Parameters.AddWithValue("$plannedDelivery", neededBy.ToString("yyyy-MM-dd"));
        purchaseOrderCommand.Parameters.AddWithValue("$totalCost", GetBasePrice(connection, transaction, productId) * quantity);
        var purchaseOrderId = Convert.ToInt32((long)purchaseOrderCommand.ExecuteScalar()!);

        using var purchaseItemCommand = connection.CreateCommand();
        purchaseItemCommand.Transaction = transaction;
        purchaseItemCommand.CommandText = """
            INSERT INTO purchase_order_items(purchase_order_id, product_id, quantity, purchase_price, supply_request_id)
            VALUES($purchaseOrderId, $productId, $quantity, $price, $supplyRequestId);
            """;
        purchaseItemCommand.Parameters.AddWithValue("$purchaseOrderId", purchaseOrderId);
        purchaseItemCommand.Parameters.AddWithValue("$productId", productId);
        purchaseItemCommand.Parameters.AddWithValue("$quantity", quantity);
        purchaseItemCommand.Parameters.AddWithValue("$price", GetBasePrice(connection, transaction, productId));
        purchaseItemCommand.Parameters.AddWithValue("$supplyRequestId", supplyRequestId);
        purchaseItemCommand.ExecuteNonQuery();

        LogAudit(connection, transaction, user.Id, "create_supply_request", "supply_requests", supplyRequestId, $"Создана заявка {number}.");
        transaction.Commit();
    }

    public List<EmployeeSummary> GetEmployees()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id,
                   u.full_name,
                   u.login,
                   r.title,
                   coalesce(ph.name, 'Без привязки'),
                   us.title,
                   u.email,
                   u.phone
            FROM users u
            JOIN roles r ON r.id = u.role_id
            JOIN user_statuses us ON us.id = u.status_id
            LEFT JOIN pharmacies ph ON ph.id = u.assigned_pharmacy_id
            WHERE r.name IN ('Admin', 'Manager')
            ORDER BY r.id DESC, u.full_name;
            """;

        var result = new List<EmployeeSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new EmployeeSummary
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Login = reader.GetString(2),
                RoleTitle = reader.GetString(3),
                Pharmacy = reader.GetString(4),
                StatusTitle = reader.GetString(5),
                Email = reader.GetString(6),
                Phone = reader.GetString(7)
            });
        }

        return result;
    }

    public void CreateEmployee(User actor, UserRole role, string login, string password, string fullName, string phone, string email, int? pharmacyId)
    {
        if (actor.Role != UserRole.Admin)
        {
            throw new InvalidOperationException("Только администратор может нанимать сотрудников.");
        }

        if (role is not (UserRole.Admin or UserRole.Manager))
        {
            throw new InvalidOperationException("Можно создавать только администраторов и менеджеров.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        EnsureLoginAvailable(connection, transaction, login);

        var userId = InsertUser(
            connection,
            transaction,
            role == UserRole.Admin ? "Admin" : "Manager",
            "Active",
            login,
            password,
            fullName,
            phone,
            email,
            role == UserRole.Manager ? pharmacyId : null,
            actor.Id);

        using var profileCommand = connection.CreateCommand();
        profileCommand.Transaction = transaction;
        profileCommand.CommandText = """
            INSERT INTO employee_profiles(user_id, personnel_number, position_title, salary, hire_note)
            VALUES($userId, $number, $positionTitle, 0, 'Создано администратором через интерфейс');
            """;
        profileCommand.Parameters.AddWithValue("$userId", userId);
        profileCommand.Parameters.AddWithValue("$number", $"EMP-{userId:D4}");
        profileCommand.Parameters.AddWithValue("$positionTitle", role == UserRole.Admin ? "Администратор сети" : "Менеджер поставок");
        profileCommand.ExecuteNonQuery();

        LogAudit(connection, transaction, actor.Id, "create_employee", "users", userId, $"Создан сотрудник {fullName}.");
        transaction.Commit();
    }

    public void ChangeEmployeeStatus(User actor, int targetUserId, string statusName)
    {
        if (actor.Role != UserRole.Admin)
        {
            throw new InvalidOperationException("Изменение статуса сотрудников доступно только администратору.");
        }

        if (targetUserId == actor.Id && !string.Equals(statusName, "Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Нельзя уволить или заблокировать самого себя.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var statusId = GetLookupId(connection, transaction, "user_statuses", statusName);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE users
            SET status_id = $statusId,
                fire_date = CASE WHEN $statusName = 'Dismissed' THEN date('now') ELSE fire_date END
            WHERE id = $userId;
            """;
        command.Parameters.AddWithValue("$statusId", statusId);
        command.Parameters.AddWithValue("$statusName", statusName);
        command.Parameters.AddWithValue("$userId", targetUserId);
        command.ExecuteNonQuery();
        LogAudit(connection, transaction, actor.Id, "change_employee_status", "users", targetUserId, $"Статус изменён на {statusName}.");
        transaction.Commit();
    }

    public List<ClientSummary> GetClients()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id,
                   u.full_name,
                   u.login,
                   u.phone,
                   u.email,
                   COUNT(o.id) AS orders_count,
                   coalesce(SUM(o.total), 0) AS total_spent,
                   coalesce(la.points, 0) AS loyalty_points,
                   us.title AS status_title
            FROM users u
            JOIN roles r ON r.id = u.role_id
            JOIN user_statuses us ON us.id = u.status_id
            LEFT JOIN orders o ON o.user_id = u.id
            LEFT JOIN loyalty_accounts la ON la.user_id = u.id
            WHERE r.name = 'Client'
            GROUP BY u.id, la.points, us.title
            ORDER BY total_spent DESC, u.full_name;
            """;

        var result = new List<ClientSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ClientSummary
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Login = reader.GetString(2),
                Phone = reader.GetString(3),
                Email = reader.GetString(4),
                OrdersCount = reader.GetInt32(5),
                TotalSpent = reader.GetDecimal(6),
                LoyaltyPoints = reader.GetInt32(7),
                StatusTitle = reader.GetString(8)
            });
        }

        return result;
    }

    public List<ActivityLogEntry> GetActivityLogs(int limit = 40)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT al.created_at,
                   coalesce(u.full_name, 'Система') AS actor,
                   al.action_type,
                   al.details
            FROM audit_logs al
            LEFT JOIN users u ON u.id = al.actor_user_id
            ORDER BY al.created_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        var result = new List<ActivityLogEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new ActivityLogEntry
            {
                OccurredAt = DateTime.Parse(reader.GetString(0)),
                Actor = reader.GetString(1),
                ActionType = reader.GetString(2),
                Details = reader.GetString(3)
            });
        }

        return result;
    }

    private void RebuildDatabase(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = DropSql + Environment.NewLine + SchemaSql + Environment.NewLine + SeedSql;
        command.ExecuteNonQuery();
    }

    private bool IsCurrentSchema(SqliteConnection connection)
    {
        if (!TableExists(connection, "app_meta"))
        {
            return false;
        }

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM app_meta WHERE key = 'schema_version';";
        var value = command.ExecuteScalar()?.ToString();
        return string.Equals(value, SchemaVersion, StringComparison.Ordinal);
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32((long)command.ExecuteScalar()!) > 0;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private IReadOnlyList<LookupItem> GetLookupItems(string sql)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var items = new List<LookupItem>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new LookupItem
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1)
            });
        }

        return items;
    }

    private static User ReadUser(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Login = reader.GetString(1),
        Password = reader.GetString(2),
        FullName = reader.GetString(3),
        Phone = reader.GetString(4),
        Email = reader.GetString(5),
        Role = Enum.Parse<UserRole>(reader.GetString(6), true),
        AssignedPharmacyId = reader.GetInt32(7),
        AssignedPharmacyName = reader.GetString(8),
        StatusName = reader.GetString(9)
    };

    private static Product ReadProduct(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        CategoryId = reader.GetInt32(1),
        PharmacyId = reader.GetInt32(2),
        ManufacturerId = reader.GetInt32(3),
        FormId = reader.GetInt32(4),
        Name = reader.GetString(5),
        Category = reader.GetString(6),
        Pharmacy = reader.GetString(7),
        Manufacturer = reader.GetString(8),
        Form = reader.GetString(9),
        PrescriptionRequired = reader.GetInt32(10) == 1,
        Price = reader.GetDecimal(11),
        Stock = reader.GetInt32(12),
        Description = reader.GetString(13)
    };

    private static void EnsureCanManageProducts(User actor)
    {
        if (actor.Role is not (UserRole.Admin or UserRole.Manager))
        {
            throw new InvalidOperationException("Управление товарами доступно только менеджеру или администратору.");
        }
    }

    private static void ValidateProduct(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name))
        {
            throw new InvalidOperationException("Название товара обязательно.");
        }

        if (product.CategoryId <= 0 || product.FormId <= 0 || product.ManufacturerId <= 0)
        {
            throw new InvalidOperationException("Для товара нужно выбрать категорию, форму и производителя.");
        }

        if (product.Price <= 0)
        {
            throw new InvalidOperationException("Цена должна быть больше нуля.");
        }

        if (product.Stock < 0)
        {
            throw new InvalidOperationException("Остаток не может быть отрицательным.");
        }
    }

    private static void EnsureLoginAvailable(SqliteConnection connection, SqliteTransaction transaction, string login)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM users WHERE lower(login) = lower($login);";
        command.Parameters.AddWithValue("$login", login.Trim());
        if (Convert.ToInt32((long)command.ExecuteScalar()!) > 0)
        {
            throw new InvalidOperationException("Логин уже занят.");
        }
    }

    private static int InsertUser(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string roleName,
        string statusName,
        string login,
        string password,
        string fullName,
        string phone,
        string email,
        int? assignedPharmacyId,
        int? createdBy)
    {
        var roleId = GetLookupId(connection, transaction, "roles", roleName);
        var statusId = GetLookupId(connection, transaction, "user_statuses", statusName);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO users(role_id, status_id, login, password, full_name, phone, email, hired_at, created_at, fire_date, assigned_pharmacy_id, created_by)
            VALUES($roleId, $statusId, $login, $password, $fullName, $phone, $email, date('now'), datetime('now'), null, $assignedPharmacyId, $createdBy);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$roleId", roleId);
        command.Parameters.AddWithValue("$statusId", statusId);
        command.Parameters.AddWithValue("$login", login.Trim());
        command.Parameters.AddWithValue("$password", password);
        command.Parameters.AddWithValue("$fullName", fullName.Trim());
        command.Parameters.AddWithValue("$phone", phone.Trim());
        command.Parameters.AddWithValue("$email", email.Trim());
        command.Parameters.AddWithValue("$assignedPharmacyId", assignedPharmacyId is null ? DBNull.Value : assignedPharmacyId.Value);
        command.Parameters.AddWithValue("$createdBy", createdBy is null ? DBNull.Value : createdBy.Value);
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    private static int GetLookupId(SqliteConnection connection, SqliteTransaction transaction, string tableName, string name)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT id FROM {tableName} WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        var value = command.ExecuteScalar();
        if (value is null)
        {
            throw new InvalidOperationException($"Не найден справочник {tableName}:{name}.");
        }

        return Convert.ToInt32((long)value);
    }

    private static int GetProductStock(SqliteConnection connection, SqliteTransaction transaction, int productId, int pharmacyId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT coalesce(SUM(quantity - reserved_quantity), 0)
            FROM inventory_balances
            WHERE product_id = $productId AND pharmacy_id = $pharmacyId;
            """;
        command.Parameters.AddWithValue("$productId", productId);
        command.Parameters.AddWithValue("$pharmacyId", pharmacyId);
        return Convert.ToInt32(command.ExecuteScalar()!);
    }

    private static decimal GetBasePrice(SqliteConnection connection, SqliteTransaction transaction, int productId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT base_price FROM products WHERE id = $productId;";
        command.Parameters.AddWithValue("$productId", productId);
        return Convert.ToDecimal(command.ExecuteScalar()!);
    }

    private static void EnsureInventoryRow(SqliteConnection connection, SqliteTransaction transaction, int productId, int pharmacyId, int stock)
    {
        using var existsCommand = connection.CreateCommand();
        existsCommand.Transaction = transaction;
        existsCommand.CommandText = """
            SELECT id
            FROM inventory_balances
            WHERE product_id = $productId AND pharmacy_id = $pharmacyId;
            """;
        existsCommand.Parameters.AddWithValue("$productId", productId);
        existsCommand.Parameters.AddWithValue("$pharmacyId", pharmacyId);
        var existing = existsCommand.ExecuteScalar();

        if (existing is null)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO inventory_balances(product_id, pharmacy_id, location_id, batch_id, quantity, reorder_level, reserved_quantity, last_restock_at)
                VALUES($productId, $pharmacyId, null, null, $quantity, 10, 0, datetime('now'));
                """;
            insertCommand.Parameters.AddWithValue("$productId", productId);
            insertCommand.Parameters.AddWithValue("$pharmacyId", pharmacyId);
            insertCommand.Parameters.AddWithValue("$quantity", stock);
            insertCommand.ExecuteNonQuery();
            return;
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE inventory_balances
            SET quantity = $quantity,
                last_restock_at = datetime('now')
            WHERE product_id = $productId AND pharmacy_id = $pharmacyId;
            """;
        updateCommand.Parameters.AddWithValue("$productId", productId);
        updateCommand.Parameters.AddWithValue("$pharmacyId", pharmacyId);
        updateCommand.Parameters.AddWithValue("$quantity", stock);
        updateCommand.ExecuteNonQuery();
    }

    private static void AddLoyaltyPoints(SqliteConnection connection, SqliteTransaction transaction, int userId, decimal total)
    {
        var points = (int)Math.Floor(total / 20m);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE loyalty_accounts
            SET points = points + $points,
                updated_at = datetime('now')
            WHERE user_id = $userId;
            INSERT INTO loyalty_transactions(account_id, points_delta, reason, created_at)
            VALUES((SELECT id FROM loyalty_accounts WHERE user_id = $userId), $points, 'order_reward', datetime('now'));
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$points", points);
        command.ExecuteNonQuery();
    }

    private static void LogAudit(SqliteConnection connection, SqliteTransaction transaction, int? actorUserId, string actionType, string targetTable, int targetId, string details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit_logs(actor_user_id, action_type, target_table, target_id, details, created_at)
            VALUES($actorUserId, $actionType, $targetTable, $targetId, $details, datetime('now'));
            """;
        command.Parameters.AddWithValue("$actorUserId", actorUserId is null ? DBNull.Value : actorUserId.Value);
        command.Parameters.AddWithValue("$actionType", actionType);
        command.Parameters.AddWithValue("$targetTable", targetTable);
        command.Parameters.AddWithValue("$targetId", targetId);
        command.Parameters.AddWithValue("$details", details);
        command.ExecuteNonQuery();
    }

    private const string DropSql = """
        PRAGMA foreign_keys = OFF;
        DROP TABLE IF EXISTS support_tickets;
        DROP TABLE IF EXISTS notifications;
        DROP TABLE IF EXISTS audit_logs;
        DROP TABLE IF EXISTS loyalty_transactions;
        DROP TABLE IF EXISTS loyalty_accounts;
        DROP TABLE IF EXISTS product_promotions;
        DROP TABLE IF EXISTS promotions;
        DROP TABLE IF EXISTS purchase_receipts;
        DROP TABLE IF EXISTS purchase_order_items;
        DROP TABLE IF EXISTS purchase_orders;
        DROP TABLE IF EXISTS supply_requests;
        DROP TABLE IF EXISTS supply_statuses;
        DROP TABLE IF EXISTS prescriptions;
        DROP TABLE IF EXISTS deliveries;
        DROP TABLE IF EXISTS delivery_statuses;
        DROP TABLE IF EXISTS payments;
        DROP TABLE IF EXISTS payment_methods;
        DROP TABLE IF EXISTS order_status_history;
        DROP TABLE IF EXISTS order_items;
        DROP TABLE IF EXISTS orders;
        DROP TABLE IF EXISTS order_statuses;
        DROP TABLE IF EXISTS inventory_movements;
        DROP TABLE IF EXISTS movement_types;
        DROP TABLE IF EXISTS inventory_balances;
        DROP TABLE IF EXISTS product_batches;
        DROP TABLE IF EXISTS products;
        DROP TABLE IF EXISTS supplier_contacts;
        DROP TABLE IF EXISTS suppliers;
        DROP TABLE IF EXISTS manufacturers;
        DROP TABLE IF EXISTS product_forms;
        DROP TABLE IF EXISTS storage_locations;
        DROP TABLE IF EXISTS pharmacy_zones;
        DROP TABLE IF EXISTS categories;
        DROP TABLE IF EXISTS pharmacies;
        DROP TABLE IF EXISTS employee_profiles;
        DROP TABLE IF EXISTS client_profiles;
        DROP TABLE IF EXISTS users;
        DROP TABLE IF EXISTS user_statuses;
        DROP TABLE IF EXISTS roles;
        DROP TABLE IF EXISTS app_meta;
        PRAGMA foreign_keys = ON;
        """;

    private const string SchemaSql = """
        CREATE TABLE app_meta(
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL
        );

        CREATE TABLE roles(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL
        );

        CREATE TABLE user_statuses(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL
        );

        CREATE TABLE pharmacies(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            address TEXT NOT NULL,
            phone TEXT NOT NULL
        );

        CREATE TABLE users(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            role_id INTEGER NOT NULL REFERENCES roles(id),
            status_id INTEGER NOT NULL REFERENCES user_statuses(id),
            login TEXT NOT NULL UNIQUE,
            password TEXT NOT NULL,
            full_name TEXT NOT NULL,
            phone TEXT NOT NULL DEFAULT '',
            email TEXT NOT NULL DEFAULT '',
            hired_at TEXT NULL,
            created_at TEXT NOT NULL,
            fire_date TEXT NULL,
            assigned_pharmacy_id INTEGER NULL REFERENCES pharmacies(id),
            created_by INTEGER NULL REFERENCES users(id)
        );

        CREATE TABLE client_profiles(
            user_id INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
            birth_date TEXT NULL,
            address TEXT NOT NULL DEFAULT '',
            bonus_level TEXT NOT NULL DEFAULT 'Silver'
        );

        CREATE TABLE employee_profiles(
            user_id INTEGER PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
            personnel_number TEXT NOT NULL UNIQUE,
            position_title TEXT NOT NULL,
            salary NUMERIC NOT NULL DEFAULT 0,
            hire_note TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE pharmacy_zones(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id) ON DELETE CASCADE,
            name TEXT NOT NULL,
            zone_type TEXT NOT NULL
        );

        CREATE TABLE storage_locations(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            zone_id INTEGER NOT NULL REFERENCES pharmacy_zones(id) ON DELETE CASCADE,
            code TEXT NOT NULL,
            UNIQUE(zone_id, code)
        );

        CREATE TABLE categories(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            parent_category_id INTEGER NULL REFERENCES categories(id),
            name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE product_forms(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE manufacturers(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            country TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE suppliers(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            inn TEXT NOT NULL DEFAULT '',
            contact_phone TEXT NOT NULL DEFAULT '',
            contact_email TEXT NOT NULL DEFAULT '',
            rating NUMERIC NOT NULL DEFAULT 0
        );

        CREATE TABLE supplier_contacts(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            supplier_id INTEGER NOT NULL REFERENCES suppliers(id) ON DELETE CASCADE,
            full_name TEXT NOT NULL,
            position TEXT NOT NULL,
            phone TEXT NOT NULL,
            email TEXT NOT NULL
        );

        CREATE TABLE products(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            category_id INTEGER NOT NULL REFERENCES categories(id),
            form_id INTEGER NOT NULL REFERENCES product_forms(id),
            manufacturer_id INTEGER NOT NULL REFERENCES manufacturers(id),
            sku TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            prescription_required INTEGER NOT NULL DEFAULT 0,
            base_price NUMERIC NOT NULL CHECK(base_price > 0),
            description TEXT NOT NULL DEFAULT '',
            is_active INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE product_batches(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
            supplier_id INTEGER NOT NULL REFERENCES suppliers(id),
            batch_number TEXT NOT NULL,
            expiration_date TEXT NOT NULL,
            purchase_price NUMERIC NOT NULL CHECK(purchase_price > 0)
        );

        CREATE TABLE inventory_balances(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
            pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id) ON DELETE CASCADE,
            location_id INTEGER NULL REFERENCES storage_locations(id),
            batch_id INTEGER NULL REFERENCES product_batches(id),
            quantity INTEGER NOT NULL DEFAULT 0 CHECK(quantity >= 0),
            reorder_level INTEGER NOT NULL DEFAULT 0,
            reserved_quantity INTEGER NOT NULL DEFAULT 0 CHECK(reserved_quantity >= 0),
            last_restock_at TEXT NULL
        );

        CREATE TABLE movement_types(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL
        );

        CREATE TABLE inventory_movements(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id INTEGER NOT NULL REFERENCES products(id),
            pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id),
            batch_id INTEGER NULL REFERENCES product_batches(id),
            movement_type_id INTEGER NOT NULL REFERENCES movement_types(id),
            quantity INTEGER NOT NULL,
            occurred_at TEXT NOT NULL,
            performed_by_user_id INTEGER NULL REFERENCES users(id),
            comment TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE order_statuses(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL
        );

        CREATE TABLE payment_methods(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL
        );

        CREATE TABLE orders(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            number TEXT NOT NULL UNIQUE,
            user_id INTEGER NULL REFERENCES users(id),
            pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id),
            status_id INTEGER NOT NULL REFERENCES order_statuses(id),
            total NUMERIC NOT NULL CHECK(total >= 0),
            created_at TEXT NOT NULL,
            delivery_method TEXT NOT NULL,
            payment_method_id INTEGER NOT NULL REFERENCES payment_methods(id),
            comment TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE order_items(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            quantity INTEGER NOT NULL CHECK(quantity > 0),
            price NUMERIC NOT NULL CHECK(price > 0)
        );

        CREATE TABLE order_status_history(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
            status_id INTEGER NOT NULL REFERENCES order_statuses(id),
            changed_at TEXT NOT NULL,
            changed_by_user_id INTEGER NULL REFERENCES users(id),
            comment TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE payments(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
            payment_method_id INTEGER NOT NULL REFERENCES payment_methods(id),
            amount NUMERIC NOT NULL CHECK(amount >= 0),
            status TEXT NOT NULL,
            paid_at TEXT NULL
        );

        CREATE TABLE delivery_statuses(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL
        );

        CREATE TABLE deliveries(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
            status_id INTEGER NOT NULL REFERENCES delivery_statuses(id),
            address TEXT NOT NULL,
            planned_at TEXT NULL,
            delivered_at TEXT NULL,
            courier_name TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE prescriptions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL REFERENCES users(id),
            product_id INTEGER NOT NULL REFERENCES products(id),
            doctor_name TEXT NOT NULL,
            issued_at TEXT NOT NULL,
            valid_until TEXT NOT NULL,
            status TEXT NOT NULL
        );

        CREATE TABLE supply_statuses(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL
        );

        CREATE TABLE supply_requests(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            number TEXT NOT NULL UNIQUE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            supplier_id INTEGER NOT NULL REFERENCES suppliers(id),
            pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id),
            requested_by_user_id INTEGER NOT NULL REFERENCES users(id),
            quantity INTEGER NOT NULL CHECK(quantity > 0),
            status_id INTEGER NOT NULL REFERENCES supply_statuses(id),
            priority TEXT NOT NULL,
            needed_by TEXT NOT NULL,
            created_at TEXT NOT NULL,
            comment TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE purchase_orders(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            number TEXT NOT NULL UNIQUE,
            supplier_id INTEGER NOT NULL REFERENCES suppliers(id),
            pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id),
            manager_user_id INTEGER NOT NULL REFERENCES users(id),
            status_id INTEGER NOT NULL REFERENCES supply_statuses(id),
            planned_delivery TEXT NULL,
            total_cost NUMERIC NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );

        CREATE TABLE purchase_order_items(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            purchase_order_id INTEGER NOT NULL REFERENCES purchase_orders(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            quantity INTEGER NOT NULL CHECK(quantity > 0),
            purchase_price NUMERIC NOT NULL CHECK(purchase_price > 0),
            supply_request_id INTEGER NULL REFERENCES supply_requests(id)
        );

        CREATE TABLE purchase_receipts(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            purchase_order_id INTEGER NOT NULL REFERENCES purchase_orders(id) ON DELETE CASCADE,
            received_at TEXT NOT NULL,
            received_by_user_id INTEGER NOT NULL REFERENCES users(id),
            comment TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE promotions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL,
            description TEXT NOT NULL DEFAULT '',
            discount_percent NUMERIC NOT NULL DEFAULT 0,
            start_date TEXT NOT NULL,
            end_date TEXT NOT NULL,
            is_active INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE product_promotions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            product_id INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
            promotion_id INTEGER NOT NULL REFERENCES promotions(id) ON DELETE CASCADE
        );

        CREATE TABLE loyalty_accounts(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
            points INTEGER NOT NULL DEFAULT 0,
            tier TEXT NOT NULL DEFAULT 'Silver',
            updated_at TEXT NOT NULL
        );

        CREATE TABLE loyalty_transactions(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            account_id INTEGER NOT NULL REFERENCES loyalty_accounts(id) ON DELETE CASCADE,
            points_delta INTEGER NOT NULL,
            reason TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        CREATE TABLE audit_logs(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            actor_user_id INTEGER NULL REFERENCES users(id),
            action_type TEXT NOT NULL,
            target_table TEXT NOT NULL,
            target_id INTEGER NOT NULL,
            details TEXT NOT NULL,
            created_at TEXT NOT NULL
        );

        CREATE TABLE notifications(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            title TEXT NOT NULL,
            body TEXT NOT NULL,
            is_read INTEGER NOT NULL DEFAULT 0,
            created_at TEXT NOT NULL
        );

        CREATE TABLE support_tickets(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
            subject TEXT NOT NULL,
            status TEXT NOT NULL,
            created_at TEXT NOT NULL,
            resolved_at TEXT NULL
        );
        """;

    private const string SeedSql = """
        INSERT INTO app_meta(key, value) VALUES ('schema_version', '4');

        INSERT INTO roles(id, name, title) VALUES
            (1, 'Client', 'Клиент'),
            (2, 'Manager', 'Менеджер'),
            (3, 'Admin', 'Администратор');

        INSERT INTO user_statuses(id, name, title) VALUES
            (1, 'Active', 'Активен'),
            (2, 'Dismissed', 'Уволен'),
            (3, 'Blocked', 'Заблокирован');

        INSERT INTO pharmacies(name, address, phone) VALUES
            ('Аптека Здоровье+', 'Новосибирск, Красный проспект, 12', '+7 383 100-10-10'),
            ('ФармМаркет 24', 'Новосибирск, ул. Ленина, 8', '+7 383 200-20-20'),
            ('Доктор рядом', 'Новосибирск, ул. Кирова, 31', '+7 383 300-30-30');

        INSERT INTO users(role_id, status_id, login, password, full_name, phone, email, hired_at, created_at, assigned_pharmacy_id, created_by) VALUES
            (3, 1, 'admin_master', 'admin2026', 'Анна Лебедева', '+7 900 000-00-01', 'admin@pharmaflow.local', date('now'), datetime('now'), null, null),
            (2, 1, 'manager_nsk1', 'mng2026A', 'Егор Соловьёв', '+7 900 000-00-11', 'manager1@pharmaflow.local', date('now'), datetime('now'), 1, 1),
            (2, 1, 'manager_nsk2', 'mng2026B', 'Мария Орлова', '+7 900 000-00-12', 'manager2@pharmaflow.local', date('now'), datetime('now'), 2, 1),
            (2, 1, 'manager_nsk3', 'mng2026C', 'Дмитрий Новиков', '+7 900 000-00-13', 'manager3@pharmaflow.local', date('now'), datetime('now'), 3, 1),
            (1, 1, 'client_demo', 'client2026', 'Иван Петров', '+7 913 123-45-67', 'client@mail.ru', null, datetime('now'), null, null);

        INSERT INTO client_profiles(user_id, birth_date, address, bonus_level) VALUES
            (5, '1998-03-14', 'Новосибирск, ул. Мичурина, 18', 'Gold');

        INSERT INTO employee_profiles(user_id, personnel_number, position_title, salary, hire_note) VALUES
            (1, 'EMP-0001', 'Главный администратор', 135000, 'Отвечает за сеть и пользователей'),
            (2, 'EMP-0002', 'Менеджер поставок', 92000, 'Аптека Здоровье+'),
            (3, 'EMP-0003', 'Менеджер поставок', 92000, 'ФармМаркет 24'),
            (4, 'EMP-0004', 'Менеджер поставок', 92000, 'Доктор рядом');

        INSERT INTO pharmacy_zones(pharmacy_id, name, zone_type) VALUES
            (1, 'Основной склад', 'warehouse'),
            (2, 'Основной склад', 'warehouse'),
            (3, 'Основной склад', 'warehouse');

        INSERT INTO storage_locations(zone_id, code) VALUES
            (1, 'A-01'),
            (2, 'A-01'),
            (3, 'A-01');

        INSERT INTO categories(parent_category_id, name) VALUES
            (null, 'Обезболивающие'),
            (null, 'Витамины'),
            (null, 'Антисептики'),
            (null, 'Противовирусные'),
            (null, 'ЖКТ'),
            (null, 'Аллергия'),
            (null, 'Сердечно-сосудистые'),
            (null, 'Детские товары');

        INSERT INTO product_forms(name) VALUES
            ('Таблетки'),
            ('Капсулы'),
            ('Раствор'),
            ('Порошок'),
            ('Спрей');

        INSERT INTO manufacturers(name, country) VALUES
            ('Фармстандарт', 'Россия'),
            ('Эвалар', 'Россия'),
            ('Bayer', 'Германия'),
            ('Renewal', 'Россия'),
            ('Sanofi', 'Франция');

        INSERT INTO suppliers(name, inn, contact_phone, contact_email, rating) VALUES
            ('СибирьФармСнаб', '5400000001', '+7 383 410-10-10', 'supply@sibpharm.ru', 4.8),
            ('МедЛогистика', '5400000002', '+7 383 420-20-20', 'office@medlog.ru', 4.6),
            ('ФармИмпорт', '5400000003', '+7 383 430-30-30', 'sales@pharmimport.ru', 4.7);

        INSERT INTO supplier_contacts(supplier_id, full_name, position, phone, email) VALUES
            (1, 'Алексей Семёнов', 'Аккаунт-менеджер', '+7 913 000-10-10', 'alexey@sibpharm.ru'),
            (2, 'Наталья Фомина', 'Логист', '+7 913 000-20-20', 'fomina@medlog.ru'),
            (3, 'Ирина Ковалёва', 'Руководитель продаж', '+7 913 000-30-30', 'ik@pharmimport.ru');

        INSERT INTO products(category_id, form_id, manufacturer_id, sku, name, prescription_required, base_price, description, is_active) VALUES
            (1, 1, 1, 'SKU-PAR500', 'Парацетамол 500 мг', 0, 89.90, 'Жаропонижающее и обезболивающее средство.', 1),
            (2, 1, 2, 'SKU-VITC1K', 'Витамин C 1000', 0, 349.00, 'Поддержка иммунитета в сезон простуд.', 1),
            (3, 3, 4, 'SKU-CHX100', 'Хлоргексидин', 0, 49.50, 'Антисептик для наружного применения.', 1),
            (4, 2, 5, 'SKU-OSL75', 'Осельтамивир', 1, 1190.00, 'Рецептурный противовирусный препарат.', 1),
            (5, 4, 3, 'SKU-SMEKTA', 'Смекта', 0, 259.00, 'Средство при расстройствах пищеварения.', 1),
            (6, 1, 4, 'SKU-CET10', 'Цетиризин', 0, 132.00, 'Антигистаминное средство.', 1),
            (1, 1, 3, 'SKU-IBU200', 'Ибупрофен 200 мг', 0, 116.00, 'НПВС при боли и температуре.', 1),
            (2, 1, 4, 'SKU-MGB6', 'Магний B6', 0, 429.00, 'Комплекс для нервной системы и мышц.', 1);

        INSERT INTO product_batches(product_id, supplier_id, batch_number, expiration_date, purchase_price) VALUES
            (1, 1, 'BATCH-001', '2027-12-31', 60.00),
            (2, 2, 'BATCH-002', '2027-10-15', 240.00),
            (3, 1, 'BATCH-003', '2028-02-01', 28.00),
            (4, 3, 'BATCH-004', '2027-08-01', 820.00),
            (5, 2, 'BATCH-005', '2027-09-12', 180.00),
            (6, 1, 'BATCH-006', '2028-01-10', 88.00),
            (7, 2, 'BATCH-007', '2027-11-11', 76.00),
            (8, 3, 'BATCH-008', '2028-03-03', 320.00);

        INSERT INTO inventory_balances(product_id, pharmacy_id, location_id, batch_id, quantity, reorder_level, reserved_quantity, last_restock_at) VALUES
            (1, 1, 1, 1, 42, 15, 0, datetime('now')),
            (2, 2, 2, 2, 18, 10, 0, datetime('now')),
            (3, 3, 3, 3, 67, 20, 0, datetime('now')),
            (4, 1, 1, 4, 9, 8, 0, datetime('now')),
            (5, 2, 2, 5, 24, 12, 0, datetime('now')),
            (6, 3, 3, 6, 35, 14, 0, datetime('now')),
            (7, 2, 2, 7, 51, 20, 0, datetime('now')),
            (8, 1, 1, 8, 14, 8, 0, datetime('now'));

        INSERT INTO products(category_id, form_id, manufacturer_id, sku, name, prescription_required, base_price, description, is_active) VALUES
            (4, 2, 3, 'SKU-INGAV', 'Ингавирин 90 мг', 0, 780.00, 'Противовирусный препарат для взрослых.', 1),
            (4, 5, 5, 'SKU-GRIPP', 'Гриппферон', 0, 455.00, 'Назальный спрей для профилактики вирусных инфекций.', 1),
            (1, 1, 1, 'SKU-NOSHPA', 'Но-шпа', 0, 219.00, 'Спазмолитик при болях и спазмах.', 1),
            (1, 1, 3, 'SKU-KETOR', 'Кеторол', 0, 169.00, 'Сильное обезболивающее средство.', 1),
            (2, 1, 2, 'SKU-OMEGA', 'Омега-3', 0, 690.00, 'Поддержка сердца и сосудов.', 1),
            (2, 1, 4, 'SKU-D3', 'Витамин D3 2000', 0, 399.00, 'Поддержка иммунитета и костной ткани.', 1),
            (3, 3, 1, 'SKU-MIRAM', 'Мирамистин', 0, 389.00, 'Антисептик широкого спектра.', 1),
            (3, 5, 5, 'SKU-TANTUM', 'Тантум Верде', 0, 472.00, 'Спрей для горла с антисептическим действием.', 1),
            (5, 2, 3, 'SKU-LINEX', 'Линекс Форте', 0, 544.00, 'Пробиотик для восстановления микрофлоры.', 1),
            (5, 4, 2, 'SKU-ENTERO', 'Энтеросгель', 0, 619.00, 'Энтеросорбент при интоксикациях.', 1),
            (6, 1, 4, 'SKU-LORAT', 'Лоратадин', 0, 98.00, 'Антигистаминный препарат без выраженной сонливости.', 1),
            (6, 1, 5, 'SKU-SUPRA', 'Супрастин', 0, 189.00, 'Антигистаминный препарат быстрого действия.', 1),
            (7, 1, 3, 'SKU-CONCOR', 'Конкор 5 мг', 1, 410.00, 'Препарат для контроля давления и пульса.', 1),
            (7, 1, 5, 'SKU-CAPOT', 'Капотен', 1, 294.00, 'Средство для снижения артериального давления.', 1),
            (8, 4, 2, 'SKU-NUROF', 'Нурофен Детский', 0, 328.00, 'Суспензия для детей при температуре и боли.', 1),
            (8, 1, 4, 'SKU-AQUAD', 'Аквадетрим', 0, 274.00, 'Витамин D для детей и взрослых.', 1);

        INSERT INTO product_batches(product_id, supplier_id, batch_number, expiration_date, purchase_price) VALUES
            (9, 3, 'BATCH-009', '2028-02-14', 520.00),
            (10, 2, 'BATCH-010', '2027-12-01', 300.00),
            (11, 1, 'BATCH-011', '2028-04-20', 150.00),
            (12, 2, 'BATCH-012', '2028-01-15', 121.00),
            (13, 3, 'BATCH-013', '2028-07-07', 490.00),
            (14, 1, 'BATCH-014', '2028-08-08', 255.00),
            (15, 1, 'BATCH-015', '2027-11-25', 270.00),
            (16, 3, 'BATCH-016', '2028-05-05', 339.00),
            (17, 2, 'BATCH-017', '2028-09-09', 390.00),
            (18, 1, 'BATCH-018', '2028-06-18', 441.00),
            (19, 2, 'BATCH-019', '2028-03-22', 61.00),
            (20, 3, 'BATCH-020', '2028-04-11', 122.00),
            (21, 1, 'BATCH-021', '2027-10-30', 320.00),
            (22, 3, 'BATCH-022', '2027-09-15', 210.00),
            (23, 2, 'BATCH-023', '2028-02-28', 245.00),
            (24, 1, 'BATCH-024', '2028-12-12', 190.00);

        INSERT INTO inventory_balances(product_id, pharmacy_id, location_id, batch_id, quantity, reorder_level, reserved_quantity, last_restock_at) VALUES
            (9, 1, 1, 9, 16, 8, 0, datetime('now')),
            (10, 2, 2, 10, 22, 10, 0, datetime('now')),
            (11, 1, 1, 11, 27, 12, 0, datetime('now')),
            (12, 3, 3, 12, 19, 10, 0, datetime('now')),
            (13, 2, 2, 13, 13, 6, 0, datetime('now')),
            (14, 1, 1, 14, 21, 10, 0, datetime('now')),
            (15, 3, 3, 15, 39, 15, 0, datetime('now')),
            (16, 2, 2, 16, 17, 8, 0, datetime('now')),
            (17, 1, 1, 17, 31, 12, 0, datetime('now')),
            (18, 3, 3, 18, 15, 8, 0, datetime('now')),
            (19, 2, 2, 19, 44, 16, 0, datetime('now')),
            (20, 1, 1, 20, 26, 12, 0, datetime('now')),
            (21, 3, 3, 21, 11, 5, 0, datetime('now')),
            (22, 2, 2, 22, 18, 7, 0, datetime('now')),
            (23, 1, 1, 23, 29, 14, 0, datetime('now')),
            (24, 3, 3, 24, 33, 14, 0, datetime('now'));

        INSERT INTO products(category_id, form_id, manufacturer_id, sku, name, prescription_required, base_price, description, is_active) VALUES
            (4, 2, 5, 'SKU-ARBID', 'Арбидол', 0, 612.00, 'Противовирусный препарат для сезонных инфекций.', 1),
            (1, 1, 1, 'SKU-CITRA', 'Цитрамон П', 0, 84.00, 'Комбинированное средство при головной боли.', 1),
            (2, 1, 2, 'SKU-COLLAG', 'Коллаген + Биотин', 0, 899.00, 'Комплекс для кожи, волос и суставов.', 1),
            (5, 1, 3, 'SKU-MEZIM', 'Мезим Форте', 0, 176.00, 'Ферментный препарат для пищеварения.', 1),
            (6, 1, 4, 'SKU-ZODAK', 'Зодак', 0, 214.00, 'Средство от аллергии для ежедневного применения.', 1),
            (8, 5, 5, 'SKU-AQUAL', 'Аквалор Беби', 0, 358.00, 'Спрей для гигиены носа у детей.', 1);

        INSERT INTO product_batches(product_id, supplier_id, batch_number, expiration_date, purchase_price) VALUES
            (25, 3, 'BATCH-025', '2028-11-10', 420.00),
            (26, 1, 'BATCH-026', '2028-10-01', 49.00),
            (27, 2, 'BATCH-027', '2029-01-12', 610.00),
            (28, 1, 'BATCH-028', '2028-07-17', 99.00),
            (29, 2, 'BATCH-029', '2028-08-03', 133.00),
            (30, 3, 'BATCH-030', '2028-09-09', 230.00);

        INSERT INTO inventory_balances(product_id, pharmacy_id, location_id, batch_id, quantity, reorder_level, reserved_quantity, last_restock_at) VALUES
            (25, 1, 1, 25, 20, 8, 0, datetime('now')),
            (26, 2, 2, 26, 38, 14, 0, datetime('now')),
            (27, 1, 1, 27, 12, 6, 0, datetime('now')),
            (28, 3, 3, 28, 23, 10, 0, datetime('now')),
            (29, 2, 2, 29, 25, 9, 0, datetime('now')),
            (30, 3, 3, 30, 16, 7, 0, datetime('now'));

        INSERT INTO movement_types(name, title) VALUES
            ('Receipt', 'Приход'),
            ('Sale', 'Продажа'),
            ('Transfer', 'Перемещение'),
            ('WriteOff', 'Списание');

        INSERT INTO order_statuses(name, title) VALUES
            ('New', 'Новый'),
            ('Processing', 'В обработке'),
            ('Ready', 'Готов к выдаче'),
            ('Closed', 'Закрыт');

        INSERT INTO payment_methods(name, title) VALUES
            ('Cash', 'Наличные'),
            ('Card', 'Карта'),
            ('CashOnDelivery', 'Оплата при получении');

        INSERT INTO delivery_statuses(name, title) VALUES
            ('Planned', 'Запланирована'),
            ('OnRoute', 'В пути'),
            ('Delivered', 'Доставлена');

        INSERT INTO supply_statuses(name, title) VALUES
            ('Open', 'Открыта'),
            ('Approved', 'Согласована'),
            ('InTransit', 'В пути'),
            ('Closed', 'Закрыта');

        INSERT INTO orders(number, user_id, pharmacy_id, status_id, total, created_at, delivery_method, payment_method_id, comment) VALUES
            ('ORD-SEED-001', 5, 1, 2, 438.90, datetime('now', '-2 days'), 'Pickup', 3, 'Тестовый заказ клиента'),
            ('ORD-SEED-002', 5, 2, 3, 259.00, datetime('now', '-1 day'), 'Courier', 2, 'Доставка до двери');

        INSERT INTO order_items(order_id, product_id, quantity, price) VALUES
            (1, 1, 1, 89.90),
            (1, 8, 1, 349.00),
            (2, 5, 1, 259.00);

        INSERT INTO order_status_history(order_id, status_id, changed_at, changed_by_user_id, comment) VALUES
            (1, 1, datetime('now', '-2 days'), 5, 'Заказ создан'),
            (1, 2, datetime('now', '-2 days', '+2 hours'), 2, 'Передан менеджеру'),
            (2, 1, datetime('now', '-1 day'), 5, 'Заказ создан'),
            (2, 3, datetime('now', '-1 day', '+5 hours'), 3, 'Подготовлен к доставке');

        INSERT INTO payments(order_id, payment_method_id, amount, status, paid_at) VALUES
            (1, 3, 438.90, 'pending', null),
            (2, 2, 259.00, 'paid', datetime('now', '-1 day', '+6 hours'));

        INSERT INTO deliveries(order_id, status_id, address, planned_at, delivered_at, courier_name) VALUES
            (2, 3, 'Новосибирск, ул. Мичурина, 18', datetime('now', '-1 day', '+4 hours'), datetime('now', '-1 day', '+6 hours'), 'Курьер Сергей');

        INSERT INTO prescriptions(user_id, product_id, doctor_name, issued_at, valid_until, status) VALUES
            (5, 4, 'Д-р Климова', datetime('now', '-10 days'), datetime('now', '+20 days'), 'valid');

        INSERT INTO supply_requests(number, product_id, supplier_id, pharmacy_id, requested_by_user_id, quantity, status_id, priority, needed_by, created_at, comment) VALUES
            ('SUP-SEED-001', 4, 3, 1, 2, 20, 2, 'Высокий', date('now', '+4 days'), datetime('now', '-1 day'), 'Нужно пополнить рецептурный остаток'),
            ('SUP-SEED-002', 2, 2, 2, 3, 40, 1, 'Средний', date('now', '+6 days'), datetime('now', '-3 hours'), 'Подготовка к сезонному спросу'),
            ('SUP-SEED-003', 6, 1, 3, 4, 30, 3, 'Высокий', date('now', '+2 days'), datetime('now', '-6 hours'), 'Поставка уже подтверждена');

        INSERT INTO purchase_orders(number, supplier_id, pharmacy_id, manager_user_id, status_id, planned_delivery, total_cost, created_at) VALUES
            ('PO-SEED-001', 3, 1, 2, 2, date('now', '+4 days'), 23800.00, datetime('now', '-1 day')),
            ('PO-SEED-002', 2, 2, 3, 1, date('now', '+6 days'), 13960.00, datetime('now', '-3 hours')),
            ('PO-SEED-003', 1, 3, 4, 3, date('now', '+2 days'), 2640.00, datetime('now', '-6 hours'));

        INSERT INTO purchase_order_items(purchase_order_id, product_id, quantity, purchase_price, supply_request_id) VALUES
            (1, 4, 20, 1190.00, 1),
            (2, 2, 40, 349.00, 2),
            (3, 6, 30, 88.00, 3);

        INSERT INTO purchase_receipts(purchase_order_id, received_at, received_by_user_id, comment) VALUES
            (3, datetime('now', '-1 hour'), 4, 'Часть поставки подтверждена по накладной');

        INSERT INTO promotions(name, description, discount_percent, start_date, end_date, is_active) VALUES
            ('Весенний иммунитет', 'Скидка на витамины и базовые антисептики', 10, date('now', '-15 days'), date('now', '+15 days'), 1);

        INSERT INTO product_promotions(product_id, promotion_id) VALUES
            (2, 1),
            (3, 1),
            (8, 1);

        INSERT INTO loyalty_accounts(user_id, points, tier, updated_at) VALUES
            (5, 120, 'Gold', datetime('now'));

        INSERT INTO loyalty_transactions(account_id, points_delta, reason, created_at) VALUES
            (1, 120, 'seed_bonus', datetime('now', '-5 days'));

        INSERT INTO notifications(user_id, title, body, is_read, created_at) VALUES
            (1, 'Контроль процессов', 'Проверьте открытые заявки поставщиков и статусы менеджеров.', 0, datetime('now')),
            (2, 'Низкий остаток', 'Осельтамивир требует срочного пополнения.', 0, datetime('now'));

        INSERT INTO support_tickets(user_id, subject, status, created_at, resolved_at) VALUES
            (5, 'Уточнить доставку по заказу ORD-SEED-002', 'closed', datetime('now', '-1 day'), datetime('now'));

        INSERT INTO audit_logs(actor_user_id, action_type, target_table, target_id, details, created_at) VALUES
            (1, 'seed_admin', 'users', 1, 'Создан базовый администратор системы.', datetime('now', '-7 days')),
            (2, 'seed_supply', 'supply_requests', 1, 'Создан пример заявки для менеджера.', datetime('now', '-1 day'));
        """;
}
