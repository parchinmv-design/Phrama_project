-- PharmaFlow seed data
INSERT INTO app_meta(key, value) VALUES ('schema_version', '2');

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
    (7, 1, 3, 'SKU-IBU200', 'Ибупрофен 200 мг', 0, 116.00, 'НПВС при боли и температуре.', 1),
    (8, 1, 4, 'SKU-MGB6', 'Магний B6', 0, 429.00, 'Комплекс для нервной системы и мышц.', 1);

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
