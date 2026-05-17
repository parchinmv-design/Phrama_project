-- Demo data for PharmaFlow
INSERT INTO roles(id, name, title) VALUES
    (1, 'Client', 'Клиент'),
    (2, 'Pharmacist', 'Фармацевт'),
    (3, 'Admin', 'Администратор');

INSERT INTO users(role_id, login, password, full_name, phone, email) VALUES
    (3, 'admin', 'admin', 'Администратор системы', '+7 900 000-00-01', 'admin@pharma.local'),
    (2, 'pharm', 'pharm', 'Фармацевт смены', '+7 900 000-00-02', 'pharm@pharma.local'),
    (1, 'client', 'client', 'Иван Петров', '+7 913 123-45-67', 'client@mail.ru');

INSERT INTO pharmacies(name, address, phone) VALUES
    ('Аптека Здоровье+', 'Новосибирск, Красный проспект, 12', '+7 383 100-10-10'),
    ('ФармМаркет 24', 'Новосибирск, ул. Ленина, 8', '+7 383 200-20-20'),
    ('Доктор рядом', 'Новосибирск, ул. Кирова, 31', '+7 383 300-30-30');

INSERT INTO categories(name) VALUES
    ('Обезболивающие'), ('Витамины'), ('Антисептики'), ('Противовирусные'), ('ЖКТ'), ('Аллергия');
