# 💼 Karobar-App (کاروبار)

## 📖 Overview

**Karobar-App** is a complete business management, billing, and inventory solution designed to help everyday business owners run their shops and warehouses smoothly. Instead of juggling paper registers (Khata), loose receipts, and manual calculators, Karobar-App brings your daily billing, stock tracking, customer accounts, supplier bills, and cash counter into one easy-to-use place.

Whether you are serving a busy line of walk-in customers at the counter or fulfilling bulk orders for regular credit buyers, Karobar-App keeps your numbers accurate and gives you a clear picture of your daily sales, stock levels, expenses, and real profit.

### What Karobar-App Does for You
* **Fast Counter Billing (POS):** Create cash or credit bills in seconds using a barcode scanner or quick keyboard shortcuts, print thermal receipts or full-page A4 invoices, and put bills on hold when a customer needs more time so the next person in line doesn't have to wait.
* **Smart Stock & Expiry Tracking:** Always know exactly what is on your shelves. Track products by pack or individual unit, manage batch numbers and expiry dates so older stock sells first, and get warned before items run out or expire.
* **Digital Customer & Supplier Khata (Ledgers):** Say goodbye to forgotten dues. Automatically track who owes you money and what you owe your suppliers, accept partial or advance payments, and print clean account statements whenever needed.
* **Cash Counter & Shift Tracking:** Keep track of every rupee or dollar entering and leaving your cash drawer during a shift—from cash sales and customer recoveries to petty shop expenses—and easily match your physical cash at closing time.
* **Clear Expenses & Profit Reports:** Record daily shop expenses like rent, electricity, salaries, and tea/refreshments, and instantly see your true net profit without complicated accounting math.

### Who Can Use Karobar-App?
Karobar-App is built to fit both **Retail** (selling single units to walk-in customers) and **Wholesale** (selling in packs and cartons with bulk discounts and credit terms), making it ideal for:

* **Pharmacies & Medical Stores:** Track medicine batches, expiry dates, strip/pack vs. tablet sales, and doctor/patient details for prescription items.
* **Wholesale & Distribution Businesses:** Manage bulk orders, bonus/free schemes, customer-specific pricing, and ongoing credit accounts.
* **Grocery Stores, Supermarkets & Karyana Stores:** Speed up checkout lines with barcode scanning, quick change calculation, and thermal receipt printing.
* **General Merchants, Hardware, Cosmetics & Auto Parts Shops:** Manage thousands of items, supplier purchases, returns, and customer balances effortlessly.
* **Traders & Multi-Counter Shops:** Give staff their own logins with controlled permissions so cashiers, storekeepers, and managers only see and do what their role allows.

---

## 🗺️ App Features Diagram

Below is a complete visual map of every module and function available inside **Karobar-App**:

```mermaid
flowchart LR
    ROOT(("💼 Karobar-App"))

    %% Main Pillars
    ROOT --> SALES["🛒 Sales & POS"]
    ROOT --> PURCHASES["🚚 Purchases & Ordering"]
    ROOT --> INVENTORY["📦 Inventory & Stock Control"]
    ROOT --> FINANCE["💵 Accounts, Khata & Cash"]
    ROOT --> REPORTS["📊 Reports & Business Insights"]
    ROOT --> ADMIN["⚙️ Admin, Security & Setup"]

    %% 1. Sales & POS
    subgraph SG_SALES["🛒 Sales & Point of Sale (POS)"]
        direction TB
        S1["⚡ Barcode POS Terminal (Fast Walk-in Checkout)"]
        S2["🏪 Retail Billing (Unit Sales & Prescriptions)"]
        S3["📦 Wholesale Billing (Packs, Cartons & Bonus Schemes)"]
        S4["⏸️ Hold & Resume Customer Draft Bills"]
        S5["↩️ Sale Returns & Customer Refunds"]
        S6["🖨️ Thermal (78mm) & A4 Invoice Printing"]
    end
    SALES --> SG_SALES

    %% 2. Purchases & Ordering
    subgraph SG_PURCHASES["🚚 Purchases & Procurement"]
        direction TB
        P1["📝 Customer Demands / Shortbook (Wanted Items)"]
        P2["📈 Smart Purchase Orders & Reorder Forecasting"]
        P3["📥 Purchase Invoices (Stock Inward, Batches & Pricing)"]
        P4["📤 Purchase Returns (Supplier Debit Notes)"]
    end
    PURCHASES --> SG_PURCHASES

    %% 3. Inventory & Stock Control
    subgraph SG_INVENTORY["📦 Products & Inventory Management"]
        direction TB
        I1["🏷️ Products, Categories & Brands Catalog"]
        I2["⏳ Batch & Expiry Tracking (FEFO & Expiry Alerts)"]
        I3["🏷️ Barcode Studio & Custom Sticker Printing"]
        I4["📋 Stocktake Sessions (Physical Count & Review)"]
        I5["⚖️ Stock Adjustments (Count Corrections)"]
        I6["🗑️ Stock Disposals & Quarantine (Damaged / Expired)"]
    end
    INVENTORY --> SG_INVENTORY

    %% 4. Accounts, Khata & Cash
    subgraph SG_FINANCE["💵 Accounts, Khata (Ledgers) & Cash"]
        direction TB
        F1["📒 Customer Ledger (Credit Dues & Statements)"]
        F2["📒 Supplier Ledger (Payables & Balances)"]
        F3["🗄️ Cash Drawer Shifts (Opening Float to Closing Count)"]
        F4["🏦 Financial Accounts (Cash, Bank & Digital Wallets)"]
        F5["🧾 Payment Vouchers & Multi-Bill Settlements"]
        F6["💸 Daily Operating Expenses (Rent, Bills, Salaries)"]
    end
    FINANCE --> SG_FINANCE

    %% 5. Reports & Business Insights
    subgraph SG_REPORTS["📊 Business Reports & Analytics"]
        direction TB
        R1["🖥️ Live Executive Dashboard"]
        R2["💰 Net Profit & Loss (P&L) Statement"]
        R3["📈 Cost of Sale & Item Profit Margin Reports"]
        R4["📑 Detailed Sales, Purchase & Returns Reports"]
        R5["📦 Current Stock, Reorder & Product History Reports"]
        R6["⚠️ Near-Expiry & Customer Aging Dues Reports"]
    end
    REPORTS --> SG_REPORTS

    %% 6. Admin, Security & Setup
    subgraph SG_ADMIN["⚙️ Administration, Security & System"]
        direction TB
        A1["👥 Staff Accounts & Role Permissions"]
        A2["🎨 Store Settings, Currency & Print Templates"]
        A3["☁️ Automated Backups & Cloud Offsite Storage"]
        A4["🔐 Software License Activation & Security"]
    end
    ADMIN --> SG_ADMIN

    %% Styling for Visual Clarity
    classDef rootStyle fill:#1e293b,stroke:#0f172a,stroke-width:3px,color:#ffffff,font-weight:bold
    classDef salesStyle fill:#dbeafe,stroke:#2563eb,stroke-width:2px,color:#1e3a8a
    classDef purchaseStyle fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#78350f
    classDef inventoryStyle fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#14532d
    classDef financeStyle fill:#f3e8ff,stroke:#9333ea,stroke-width:2px,color:#581c87
    classDef reportStyle fill:#ffe4e6,stroke:#e11d48,stroke-width:2px,color:#881337
    classDef adminStyle fill:#e0f2fe,stroke:#0284c7,stroke-width:2px,color:#0c4a6e

    class ROOT rootStyle
    class SALES,S1,S2,S3,S4,S5,S6 salesStyle
    class PURCHASES,P1,P2,P3,P4 purchaseStyle
    class INVENTORY,I1,I2,I3,I4,I5,I6 inventoryStyle
    class FINANCE,F1,F2,F3,F4,F5,F6 financeStyle
    class REPORTS,R1,R2,R3,R4,R5,R6 reportStyle
    class ADMIN,A1,A2,A3,A4 adminStyle
```

---

## 🗺️ App Features Diagram

Below is a complete visual map of every module and function available inside **Karobar-App**:

```mermaid
flowchart LR
    ROOT(("💼 Karobar-App"))

    %% Main Pillars
    ROOT --> SALES["🛒 Sales & POS"]
    ROOT --> PURCHASES["🚚 Purchases & Ordering"]
    ROOT --> INVENTORY["📦 Inventory & Stock Control"]
    ROOT --> FINANCE["💵 Accounts, Khata & Cash"]
    ROOT --> REPORTS["📊 Reports & Business Insights"]
    ROOT --> ADMIN["⚙️ Admin, Security & Setup"]

    %% 1. Sales & POS
    subgraph SG_SALES["🛒 Sales & Point of Sale (POS)"]
        direction TB
        S1["⚡ Barcode POS Terminal (Fast Walk-in Checkout)"]
        S2["🏪 Retail Billing (Unit Sales & Prescriptions)"]
        S3["📦 Wholesale Billing (Packs, Cartons & Bonus Schemes)"]
        S4["⏸️ Hold & Resume Customer Draft Bills"]
        S5["↩️ Sale Returns & Customer Refunds"]
        S6["🖨️ Thermal (78mm) & A4 Invoice Printing"]
    end
    SALES --> SG_SALES

    %% 2. Purchases & Ordering
    subgraph SG_PURCHASES["🚚 Purchases & Procurement"]
        direction TB
        P1["📝 Customer Demands / Shortbook (Wanted Items)"]
        P2["📈 Smart Purchase Orders & Reorder Forecasting"]
        P3["📥 Purchase Invoices (Stock Inward, Batches & Pricing)"]
        P4["📤 Purchase Returns (Supplier Debit Notes)"]
    end
    PURCHASES --> SG_PURCHASES

    %% 3. Inventory & Stock Control
    subgraph SG_INVENTORY["📦 Products & Inventory Management"]
        direction TB
        I1["🏷️ Products, Categories & Brands Catalog"]
        I2["⏳ Batch & Expiry Tracking (FEFO & Expiry Alerts)"]
        I3["🏷️ Barcode Studio & Custom Sticker Printing"]
        I4["📋 Stocktake Sessions (Physical Count & Review)"]
        I5["⚖️ Stock Adjustments (Count Corrections)"]
        I6["🗑️ Stock Disposals & Quarantine (Damaged / Expired)"]
    end
    INVENTORY --> SG_INVENTORY

    %% 4. Accounts, Khata & Cash
    subgraph SG_FINANCE["💵 Accounts, Khata (Ledgers) & Cash"]
        direction TB
        F1["📒 Customer Ledger (Credit Dues & Statements)"]
        F2["📒 Supplier Ledger (Payables & Balances)"]
        F3["🗄️ Cash Drawer Shifts (Opening Float to Closing Count)"]
        F4["🏦 Financial Accounts (Cash, Bank & Digital Wallets)"]
        F5["🧾 Payment Vouchers & Multi-Bill Settlements"]
        F6["💸 Daily Operating Expenses (Rent, Bills, Salaries)"]
    end
    FINANCE --> SG_FINANCE

    %% 5. Reports & Business Insights
    subgraph SG_REPORTS["📊 Business Reports & Analytics"]
        direction TB
        R1["🖥️ Live Executive Dashboard"]
        R2["💰 Net Profit & Loss (P&L) Statement"]
        R3["📈 Cost of Sale & Item Profit Margin Reports"]
        R4["📑 Detailed Sales, Purchase & Returns Reports"]
        R5["📦 Current Stock, Reorder & Product History Reports"]
        R6["⚠️ Near-Expiry & Customer Aging Dues Reports"]
    end
    REPORTS --> SG_REPORTS

    %% 6. Admin, Security & Setup
    subgraph SG_ADMIN["⚙️ Administration, Security & System"]
        direction TB
        A1["👥 Staff Accounts & Role Permissions"]
        A2["🎨 Store Settings, Currency & Print Templates"]
        A3["☁️ Automated Backups & Cloud Offsite Storage"]
        A4["🔐 Software License Activation & Security"]
    end
    ADMIN --> SG_ADMIN

    %% Styling for Visual Clarity
    classDef rootStyle fill:#1e293b,stroke:#0f172a,stroke-width:3px,color:#ffffff,font-weight:bold
    classDef salesStyle fill:#dbeafe,stroke:#2563eb,stroke-width:2px,color:#1e3a8a
    classDef purchaseStyle fill:#fef3c7,stroke:#d97706,stroke-width:2px,color:#78350f
    classDef inventoryStyle fill:#dcfce7,stroke:#16a34a,stroke-width:2px,color:#14532d
    classDef financeStyle fill:#f3e8ff,stroke:#9333ea,stroke-width:2px,color:#581c87
    classDef reportStyle fill:#ffe4e6,stroke:#e11d48,stroke-width:2px,color:#881337
    classDef adminStyle fill:#e0f2fe,stroke:#0284c7,stroke-width:2px,color:#0c4a6e

    class ROOT rootStyle
    class SALES,S1,S2,S3,S4,S5,S6 salesStyle
    class PURCHASES,P1,P2,P3,P4 purchaseStyle
    class INVENTORY,I1,I2,I3,I4,I5,I6 inventoryStyle
    class FINANCE,F1,F2,F3,F4,F5,F6 financeStyle
    class REPORTS,R1,R2,R3,R4,R5,R6 reportStyle
    class ADMIN,A1,A2,A3,A4 adminStyle
```

