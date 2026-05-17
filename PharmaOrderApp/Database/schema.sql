-- PharmaFlow database schema
PRAGMA foreign_keys = ON;

CREATE TABLE roles(
    id INTEGER PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    title TEXT NOT NULL
);

CREATE TABLE users(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    role_id INTEGER NOT NULL REFERENCES roles(id),
    login TEXT NOT NULL UNIQUE,
    password TEXT NOT NULL,
    full_name TEXT NOT NULL,
    phone TEXT NOT NULL DEFAULT '',
    email TEXT NOT NULL DEFAULT ''
);

CREATE TABLE pharmacies(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    address TEXT NOT NULL DEFAULT '',
    phone TEXT NOT NULL DEFAULT ''
);

CREATE TABLE categories(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE
);

CREATE TABLE products(
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

CREATE TABLE orders(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    number TEXT NOT NULL UNIQUE,
    user_id INTEGER NULL REFERENCES users(id),
    pharmacy_id INTEGER NOT NULL REFERENCES pharmacies(id),
    status TEXT NOT NULL,
    total NUMERIC NOT NULL,
    created_at TEXT NOT NULL
);

CREATE TABLE order_items(
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    order_id INTEGER NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    product_id INTEGER NOT NULL REFERENCES products(id),
    quantity INTEGER NOT NULL CHECK(quantity > 0),
    price NUMERIC NOT NULL CHECK(price > 0)
);
