# ERD PharmaFlow

```mermaid
erDiagram
    roles ||--o{ users : "sets role"
    users ||--o{ orders : "creates"
    pharmacies ||--o{ products : "stores"
    pharmacies ||--o{ orders : "fulfills"
    categories ||--o{ products : "groups"
    orders ||--o{ order_items : "contains"
    products ||--o{ order_items : "ordered as"

    roles {
        int id PK
        string name
        string title
    }
    users {
        int id PK
        int role_id FK
        string login
        string password
        string full_name
        string phone
        string email
    }
    pharmacies {
        int id PK
        string name
        string address
        string phone
    }
    categories {
        int id PK
        string name
    }
    products {
        int id PK
        int category_id FK
        int pharmacy_id FK
        string name
        string manufacturer
        string form
        bool prescription_required
        decimal price
        int stock
        string description
    }
    orders {
        int id PK
        string number
        int user_id FK
        int pharmacy_id FK
        string status
        decimal total
        datetime created_at
    }
    order_items {
        int id PK
        int order_id FK
        int product_id FK
        int quantity
        decimal price
    }
```

## Covered processes

- Login with role check: guest, client, pharmacist, admin.
- Catalog list with search, filtering and sorting in real time.
- Product card with full object information.
- Add/edit/delete products for privileged roles.
- Cart and order creation with stock validation.
- SQLite database auto-creation and seed data import on first run.
