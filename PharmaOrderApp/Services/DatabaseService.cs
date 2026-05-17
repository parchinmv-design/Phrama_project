using Microsoft.Data.Sqlite;
using PharmaOrderApp.Models;
using System.IO;

namespace PharmaOrderApp.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService()
    {
        var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PharmaOrderApp");
        Directory.CreateDirectory(appFolder);
        var dbPath = Path.Combine(appFolder, "pharma.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        command.ExecuteNonQuery();
        SeedIfEmpty(connection);
    }

    public User? Authenticate(string login, string password)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id, u.login, u.password, u.full_name, u.phone, u.email, r.name AS role
            FROM users u
            JOIN roles r ON r.id = u.role_id
            WHERE lower(u.login) = lower($login) AND u.password = $password;
            """;
        command.Parameters.AddWithValue("$login", login.Trim());
        command.Parameters.AddWithValue("$password", password);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadUser(reader) : null;
    }

    public IReadOnlyList<string> GetCategories()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM categories ORDER BY name;";
        return ReadStringList(command);
    }

    public IReadOnlyList<string> GetPharmacies()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pharmacies ORDER BY name;";
        return ReadStringList(command);
    }

    public List<Product> SearchProducts(string search, string category, string pharmacy, string sort)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var orderBy = sort switch
        {
            "Цена ↑" => "p.price ASC",
            "Цена ↓" => "p.price DESC",
            "Остаток ↓" => "p.stock DESC",
            _ => "p.name ASC"
        };
        command.CommandText = $"""
            SELECT p.id, p.name, c.name AS category, ph.name AS pharmacy, p.manufacturer,
                   p.form, p.prescription_required, p.price, p.stock, p.description
            FROM products p
            JOIN categories c ON c.id = p.category_id
            JOIN pharmacies ph ON ph.id = p.pharmacy_id
            WHERE ($search = '' OR lower(p.name) LIKE '%' || lower($search) || '%' OR lower(p.manufacturer) LIKE '%' || lower($search) || '%')
              AND ($category = 'Все категории' OR c.name = $category)
              AND ($pharmacy = 'Все аптеки' OR ph.name = $pharmacy)
            ORDER BY {orderBy};
            """;
        command.Parameters.AddWithValue("$search", search.Trim());
        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$pharmacy", pharmacy);

        var result = new List<Product>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(ReadProduct(reader));
        }
        return result;
    }

    public Product? GetProduct(int id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.name, c.name AS category, ph.name AS pharmacy, p.manufacturer,
                   p.form, p.prescription_required, p.price, p.stock, p.description
            FROM products p
            JOIN categories c ON c.id = p.category_id
            JOIN pharmacies ph ON ph.id = p.pharmacy_id
            WHERE p.id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadProduct(reader) : null;
    }

    public void SaveProduct(Product product)
    {
        ValidateProduct(product);
        using var connection = OpenConnection();
        var categoryId = EnsureLookup(connection, "categories", product.Category);
        var pharmacyId = EnsureLookup(connection, "pharmacies", product.Pharmacy);
        using var command = connection.CreateCommand();
        command.CommandText = product.Id == 0
            ? """
              INSERT INTO products(name, category_id, pharmacy_id, manufacturer, form, prescription_required, price, stock, description)
              VALUES($name, $categoryId, $pharmacyId, $manufacturer, $form, $prescriptionRequired, $price, $stock, $description);
              """
            : """
              UPDATE products
              SET name = $name, category_id = $categoryId, pharmacy_id = $pharmacyId, manufacturer = $manufacturer,
                  form = $form, prescription_required = $prescriptionRequired, price = $price, stock = $stock, description = $description
              WHERE id = $id;
              """;
        command.Parameters.AddWithValue("$id", product.Id);
        command.Parameters.AddWithValue("$name", product.Name.Trim());
        command.Parameters.AddWithValue("$categoryId", categoryId);
        command.Parameters.AddWithValue("$pharmacyId", pharmacyId);
        command.Parameters.AddWithValue("$manufacturer", product.Manufacturer.Trim());
        command.Parameters.AddWithValue("$form", product.Form.Trim());
        command.Parameters.AddWithValue("$prescriptionRequired", product.PrescriptionRequired ? 1 : 0);
        command.Parameters.AddWithValue("$price", product.Price);
        command.Parameters.AddWithValue("$stock", product.Stock);
        command.Parameters.AddWithValue("$description", product.Description.Trim());
        command.ExecuteNonQuery();
    }

    public void DeleteProduct(int productId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM products WHERE id = $id;";
        command.Parameters.AddWithValue("$id", productId);
        command.ExecuteNonQuery();
    }

    public int CreateOrder(User user, IEnumerable<CartItem> cart)
    {
        var items = cart.Where(x => x.Quantity > 0).ToList();
        if (items.Count == 0)
        {
            throw new InvalidOperationException("Корзина пуста.");
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var item in items)
        {
            var fresh = GetProductStock(connection, item.Product.Id);
            if (fresh < item.Quantity)
            {
                throw new InvalidOperationException($"Недостаточно товара: {item.Product.Name}. Остаток: {fresh}.");
            }
        }

        var number = $"PH-{DateTime.Now:yyyyMMdd-HHmmss}";
        var total = items.Sum(x => x.Total);
        using var orderCommand = connection.CreateCommand();
        orderCommand.Transaction = transaction;
        orderCommand.CommandText = """
            INSERT INTO orders(number, user_id, pharmacy_id, status, total, created_at)
            VALUES($number, $userId, (SELECT pharmacy_id FROM products WHERE id = $firstProductId), 'Новый', $total, datetime('now'));
            SELECT last_insert_rowid();
            """;
        orderCommand.Parameters.AddWithValue("$number", number);
        orderCommand.Parameters.AddWithValue("$userId", user.Role == UserRole.Guest ? DBNull.Value : user.Id);
        orderCommand.Parameters.AddWithValue("$firstProductId", items[0].Product.Id);
        orderCommand.Parameters.AddWithValue("$total", total);
        var orderId = Convert.ToInt32((long)orderCommand.ExecuteScalar()!);

        foreach (var item in items)
        {
            using var itemCommand = connection.CreateCommand();
            itemCommand.Transaction = transaction;
            itemCommand.CommandText = """
                INSERT INTO order_items(order_id, product_id, quantity, price)
                VALUES($orderId, $productId, $quantity, $price);
                UPDATE products SET stock = stock - $quantity WHERE id = $productId;
                """;
            itemCommand.Parameters.AddWithValue("$orderId", orderId);
            itemCommand.Parameters.AddWithValue("$productId", item.Product.Id);
            itemCommand.Parameters.AddWithValue("$quantity", item.Quantity);
            itemCommand.Parameters.AddWithValue("$price", item.Product.Price);
            itemCommand.ExecuteNonQuery();
        }

        transaction.Commit();
        return orderId;
    }

    public List<Order> GetOrders(User user)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var isAdmin = user.Role is UserRole.Admin or UserRole.Pharmacist;
        command.CommandText = """
            SELECT o.id, o.number, coalesce(u.full_name, 'Гость') AS client_name, ph.name AS pharmacy,
                   o.status, o.total, o.created_at
            FROM orders o
            LEFT JOIN users u ON u.id = o.user_id
            JOIN pharmacies ph ON ph.id = o.pharmacy_id
            WHERE $isAdmin = 1 OR o.user_id = $userId
            ORDER BY o.created_at DESC;
            """;
        command.Parameters.AddWithValue("$isAdmin", isAdmin ? 1 : 0);
        command.Parameters.AddWithValue("$userId", user.Id);
        var orders = new List<Order>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            orders.Add(new Order
            {
                Id = reader.GetInt32(0),
                Number = reader.GetString(1),
                ClientName = reader.GetString(2),
                Pharmacy = reader.GetString(3),
                Status = reader.GetString(4),
                Total = reader.GetDecimal(5),
                CreatedAt = DateTime.Parse(reader.GetString(6))
            });
        }
        return orders;
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

    private static IReadOnlyList<string> ReadStringList(SqliteCommand command)
    {
        var result = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static User ReadUser(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Login = reader.GetString(1),
        Password = reader.GetString(2),
        FullName = reader.GetString(3),
        Phone = reader.GetString(4),
        Email = reader.GetString(5),
        Role = Enum.Parse<UserRole>(reader.GetString(6), ignoreCase: true)
    };

    private static Product ReadProduct(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Name = reader.GetString(1),
        Category = reader.GetString(2),
        Pharmacy = reader.GetString(3),
        Manufacturer = reader.GetString(4),
        Form = reader.GetString(5),
        PrescriptionRequired = reader.GetInt32(6) == 1,
        Price = reader.GetDecimal(7),
        Stock = reader.GetInt32(8),
        Description = reader.GetString(9)
    };

    private static int EnsureLookup(SqliteConnection connection, string table, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Категория и аптека обязательны.");
        }

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT OR IGNORE INTO {table}(name) VALUES($name);";
        insert.Parameters.AddWithValue("$name", name.Trim());
        insert.ExecuteNonQuery();

        using var select = connection.CreateCommand();
        select.CommandText = $"SELECT id FROM {table} WHERE name = $name;";
        select.Parameters.AddWithValue("$name", name.Trim());
        return Convert.ToInt32((long)select.ExecuteScalar()!);
    }

    private static int GetProductStock(SqliteConnection connection, int productId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stock FROM products WHERE id = $id;";
        command.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32((long)command.ExecuteScalar()!);
    }

    private static void ValidateProduct(Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name)) throw new InvalidOperationException("Название товара обязательно.");
        if (product.Price <= 0) throw new InvalidOperationException("Цена должна быть больше нуля.");
        if (product.Stock < 0) throw new InvalidOperationException("Остаток не может быть отрицательным.");
    }

    private static void SeedIfEmpty(SqliteConnection connection)
    {
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM products;";
        if (Convert.ToInt32((long)count.ExecuteScalar()!) > 0)
        {
            return;
        }

        using var seed = connection.CreateCommand();
        seed.CommandText = SeedSql;
        seed.ExecuteNonQuery();
    }

    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS roles(
            id INTEGER PRIMARY KEY,
            name TEXT NOT NULL UNIQUE,
            title TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS users(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            role_id INTEGER NOT NULL REFERENCES roles(id),
            login TEXT NOT NULL UNIQUE,
            password TEXT NOT NULL,
            full_name TEXT NOT NULL,
            phone TEXT NOT NULL DEFAULT '',
            email TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS pharmacies(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE,
            address TEXT NOT NULL DEFAULT '',
            phone TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS categories(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL UNIQUE
        );

        CREATE TABLE IF NOT EXISTS products(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            category_id INTEGER NOT NULL REFERENCES categories(id),
            pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id),
            name TEXT NOT NULL,
            manufacturer TEXT NOT NULL DEFAULT '',
            form TEXT NOT NULL DEFAULT '',
            prescription_required INTEGER NOT NULL DEFAULT 0,
            price NUMERIC NOT NULL CHECK(price > 0),
            stock INTEGER NOT NULL CHECK(stock >= 0),
            description TEXT NOT NULL DEFAULT ''
        );

        CREATE TABLE IF NOT EXISTS orders(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            number TEXT NOT NULL UNIQUE,
            user_id INTEGER NULL REFERENCES users(id),
            pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id),
            status TEXT NOT NULL,
            total NUMERIC NOT NULL,
            created_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS order_items(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            quantity INTEGER NOT NULL CHECK(quantity > 0),
            price NUMERIC NOT NULL CHECK(price > 0)
        );
        """;

    private const string SeedSql = """
        INSERT OR IGNORE INTO roles(id, name, title) VALUES
            (1, 'Client', 'Клиент'),
            (2, 'Pharmacist', 'Фармацевт'),
            (3, 'Admin', 'Администратор');

        INSERT OR IGNORE INTO users(role_id, login, password, full_name, phone, email) VALUES
            (3, 'admin', 'admin', 'Администратор системы', '+7 900 000-00-01', 'admin@pharma.local'),
            (2, 'pharm', 'pharm', 'Фармацевт смены', '+7 900 000-00-02', 'pharm@pharma.local'),
            (1, 'client', 'client', 'Иван Петров', '+7 913 123-45-67', 'client@mail.ru');

        INSERT OR IGNORE INTO pharmacies(name, address, phone) VALUES
            ('Аптека Здоровье+', 'Новосибирск, Красный проспект, 12', '+7 383 100-10-10'),
            ('ФармМаркет 24', 'Новосибирск, ул. Ленина, 8', '+7 383 200-20-20'),
            ('Доктор рядом', 'Новосибирск, ул. Кирова, 31', '+7 383 300-30-30');

        INSERT OR IGNORE INTO categories(name) VALUES
            ('Обезболивающие'), ('Витамины'), ('Антисептики'), ('Противовирусные'), ('ЖКТ'), ('Аллергия');

        INSERT INTO products(category_id, pharmacy_id, name, manufacturer, form, prescription_required, price, stock, description) VALUES
            ((SELECT id FROM categories WHERE name='Обезболивающие'), (SELECT id FROM pharmacies WHERE name='Аптека Здоровье+'), 'Парацетамол 500 мг', 'Фармстандарт', 'таблетки', 0, 89.90, 42, 'Жаропонижающее и обезболивающее средство.'),
            ((SELECT id FROM categories WHERE name='Витамины'), (SELECT id FROM pharmacies WHERE name='ФармМаркет 24'), 'Витамин C 1000', 'Эвалар', 'шипучие таблетки', 0, 349.00, 18, 'Поддержка иммунитета в сезон простуд.'),
            ((SELECT id FROM categories WHERE name='Антисептики'), (SELECT id FROM pharmacies WHERE name='Доктор рядом'), 'Хлоргексидин', 'Росбио', 'раствор', 0, 49.50, 67, 'Антисептик для наружного применения.'),
            ((SELECT id FROM categories WHERE name='Противовирусные'), (SELECT id FROM pharmacies WHERE name='Аптека Здоровье+'), 'Осельтамивир', 'Биохимик', 'капсулы', 1, 1190.00, 9, 'Рецептурный противовирусный препарат.'),
            ((SELECT id FROM categories WHERE name='ЖКТ'), (SELECT id FROM pharmacies WHERE name='ФармМаркет 24'), 'Смекта', 'Ipsen', 'порошок', 0, 259.00, 24, 'Средство при расстройствах пищеварения.'),
            ((SELECT id FROM categories WHERE name='Аллергия'), (SELECT id FROM pharmacies WHERE name='Доктор рядом'), 'Цетиризин', 'Озон', 'таблетки', 0, 132.00, 35, 'Антигистаминное средство.'),
            ((SELECT id FROM categories WHERE name='Обезболивающие'), (SELECT id FROM pharmacies WHERE name='ФармМаркет 24'), 'Ибупрофен 200 мг', 'Синтез', 'таблетки', 0, 116.00, 51, 'НПВС при боли и температуре.'),
            ((SELECT id FROM categories WHERE name='Витамины'), (SELECT id FROM pharmacies WHERE name='Аптека Здоровье+'), 'Магний B6', 'Renewal', 'таблетки', 0, 429.00, 14, 'Комплекс для нервной системы и мышц.');
        """;
}
