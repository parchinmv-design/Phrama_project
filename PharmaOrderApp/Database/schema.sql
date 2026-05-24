-- PharmaFlow schema
PRAGMA foreign_keys = ON;

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
