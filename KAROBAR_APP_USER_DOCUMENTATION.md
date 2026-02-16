# KAROBAR APP - COMPREHENSIVE USER DOCUMENTATION

## Table of Contents
1. [Getting Started](#getting-started)
2. [Dashboard](#dashboard)
3. [Product Management](#product-management)
4. [Purchase Management](#purchase-management)
5. [Sales Management](#sales-management)
6. [Inventory & Stock](#inventory--stock)
7. [Reports](#reports)
8. [Settings & Customization](#settings--customization)
9. [User Management & Security](#user-management--security)
10. [Keyboard Shortcuts](#keyboard-shortcuts)
11. [Troubleshooting](#troubleshooting)

---

## Getting Started

### Account Setup & Login

**Accessing Your Store:**
- Your store is hosted on a unique subdomain: `yourstore.yourdomain.com`
- You will receive login credentials (email and password) after purchasing a plan
- Navigate to your subdomain and enter your credentials

**First-Time Login:**
1. Visit your store's unique URL
2. Enter your email and password
3. Click "Login" to access the dashboard

**Password Reset:**
- Click "Forgot Password" on the login page
- Enter your registered email
- Check your email for reset instructions
- Create a new secure password

---

## Dashboard

### Overview
The Dashboard provides a real-time snapshot of your business performance with interactive visualizations and key metrics.

### Key Components

**Date Range Filter:**
- **Today** - Shows current day's data
- **This Month** - Current month statistics
- **Last 7 Days** - Week overview
- **Last 30 Days** - Month overview
- **Custom Range** - Select specific dates

**Summary Cards:**
| Card | Description |
|------|-------------|
| **Sales** | Total sales amount in selected period |
| **Purchases** | Total purchase amount in selected period |
| **Sale Returns** | Total value of returned sales |
| **Purchase Returns** | Total value of returned purchases |

**KPI Metrics:**
- **Net Sales** - Sales minus returns (real revenue)
- **Invoices** - Total invoice count (sales + purchases)
- **Products** - Active product count
- **Suppliers** - Total supplier count
- **Near Expiry** - Products expiring soon
- **Brands** - Total brand count

**Charts:**
1. **Sales Distribution (Pie Chart)** - Sales vs Purchases ratio
2. **Sales vs Purchases (Bar Chart)** - Comparative analysis
3. **Returns Trend (Area Chart)** - Return patterns over time

**Near Expiry Table:**
- Shows products expiring within selected months (1, 3, 6, 12, or 18 months)
- Filter by supplier or brand
- Columns: Product, Supplier, Brand, Batch #, Expiry Date, Quantity

---

## Product Management

### Products Overview
The Products module is the heart of your inventory system, managing your entire product catalog with detailed tracking.

### Adding a New Product

**Navigation:** Products → Add Product (or Alt+N shortcut)

**Required Fields:**
| Field | Description |
|-------|-------------|
| **Product Code** | Unique identifier (auto-generated) |
| **Product Name** | Full product name |
| **Category** | Product category (select from dropdown) |
| **Brand** | Product brand (select from dropdown) |
| **Supplier** | Primary supplier (optional) |
| **Pack Size** | Units per pack (e.g., 10 tablets) |
| **Barcode** | Product barcode (unique | auto-generated) |

**Pricing Fields:**
| Field | Description |
|-------|-------------|
| **Pack Purchase Price** | Cost price per pack |
| **Pack Sale Price** | Selling price per pack |
| **Unit Purchase Price** | Auto-calculated (Pack Price ÷ Pack Size) |
| **Unit Sale Price** | Auto-calculated |
| **Max Discount** | Maximum allowed discount percentage |

**Additional Fields:**
- **Formulation** - Product composition/details
- **Description** - Additional product information
- **Rack** - Storage location in warehouse
- **Narcotic** - Yes/No flag for controlled substances
- **Product Image** - Upload product photo

### Managing Products

**Product List Features:**
- **Search** - Find products by name
- **Filter by Brand** - Narrow down by brand
- **Filter by Supplier** - Filter by supplier
- **Pagination** - Show 10/25/50/100 items per page
- **Selection** - Checkbox selection for bulk operations

**Bulk Operations:**
1. **Bulk Edit** - Edit multiple products simultaneously
   - Select products using checkboxes
   - Click "Edit" button
   - Modify common fields (category, brand, supplier)
   - Save changes

2. **Bulk Delete** - Remove multiple products
   - Select products (only products with zero quantity can be deleted)
   - Click "Delete" button
   - Confirm with password
   - Products with stock or batches cannot be deleted

**Product Status Indicators:**
- **Green Badge** - Product has stock (shows quantity)
- **Orange Badge** - Product out of stock

### Import/Export Products

**Export to CSV:**
- Click "Export" button on Products page
- Downloads complete product catalog as CSV
- Useful for backups and external analysis

**Import from CSV:**
- Click "Import" button
- Upload CSV file with product data
- System validates and imports products
- Download template for proper format

### Categories

**Purpose:** Organize products into logical groups for better inventory management and reporting (e.g., Medicines, Cosmetics, Medical Equipment, Consumables)

**Operations:**
- Add new product categories
- Edit existing category names
- Delete unused categories (if no products assigned)
- Categories are used for filtering products and reports

**Category Details:**
| Field | Description |
|-------|-------------|
| **Category Name** | Unique name for the product group (e.g., "Antibiotics", "Surgical Items") |
| **Description** | Optional details about the category |

**Usage in System:**
- Required field when creating new products
- Used for filtering product lists and reports
- Helps organize inventory into logical groups
- Appears on invoices and reports for product classification

### Brands

**Purpose:** Track product manufacturers and suppliers for brand-based inventory management and reporting

**Operations:**
- Add new brand names
- Edit brand information
- Delete brands (if no products assigned)
- Import brands via CSV for bulk entry
- View products associated with each brand

**Brand Details:**
| Field | Description |
|-------|-------------|
| **Brand Name** | Manufacturer or brand name (e.g., "Pfizer", "GSK", "Local Pharma") |
| **Description** | Optional additional information about the brand |

**Usage in System:**
- Required field when creating new products
- Used for filtering products by manufacturer
- Helps track which brands are performing well
- Useful for supplier negotiations and brand analysis
- Appears on product labels and invoices

### Suppliers

**Purpose:** Manage vendors and product sources

**Operations:**
- Add new suppliers with contact details
- Edit supplier information (name, phone, address, email)
- Delete suppliers (if no purchase history exists)
- Import suppliers via CSV
- View supplier purchase history and total transactions

**Supplier Details:**
| Field | Description |
|-------|-------------|
| **Supplier Name** | Company or vendor name |
| **Contact Person** | Primary contact name |
| **Phone** | Contact phone number |
| **Email** | Email address |
| **Address** | Physical address |
| **City/Region** | Location details |

### Customers

**Purpose:** Track buyers and manage customer relationships

**Operations:**
- Add new customer profiles
- Edit customer information
- Delete customers (if no sales history exists)
- Import customers via CSV
- Set wholesale pricing tiers per customer
- View complete purchase history

**Customer Details:**
| Field | Description |
|-------|-------------|
| **Customer Name** | Full name or business name |
| **Phone** | Primary contact number |
| **Email** | Email address |
| **Address** | Billing/delivery address |
---

## Purchase Management

### Purchase Invoices

**Purpose:** Record all inventory purchases from suppliers

**Creating a Purchase Invoice:**

**Navigation:** Purchase Invoices → Add Invoice (or Alt+N)

**Header Information:**
| Field | Description |
|-------|-------------|
| **Invoice Type** | Cash or Credit |
| **Posted Number** | Auto-generated unique ID (PI-XXXXXX) |
| **Posted Date** | Date of purchase |
| **Supplier** | Select from dropdown (searchable) |
| **Invoice Number** | Supplier's invoice number |
| ** Invoice Amount** | Total Invoice Amount |
| **Difference** | The difference between Invoice Amount and Invoice Total Invoice will not be saved if difference is greater then 5 |

**Adding Items:**
1. Click "Search Product" Field to open popup to search a product
2. Select product from dropdown
3. System auto-fills:
   - Pack Size
   - Pack Purchase Price if available
   - Unit Purchase Price if available
   - Pack Sale Price if available
   - Unit Sale Price if available
   - Whole Sale Pack Price if available
   - Whole Sale Unit price if available
   - Margin % automatically calculated
   - Whole sale Margin % automatically calculated
   - Avg(unit purchase) automatically calcuated
   - Subtotal automatically calulated
4. Enter:
   - **Pack Quantity** - Number of packs to purchase (Unit Quanity will be automatically calculated and vice versa)
   - **Batch Number** - Product batch Number
   - **Expiry Date** - Product expiration date
   - **Item Discount %** - Discount on this item (optional)
   - **Pack Bonus** - Any pack bonus you got (unit bonus will be calulated automatically and vice versa) 
5. Click "Add to Invoice"

**Invoice Summary:**
- **Discount** -Both in % and Amount 
- **Tax** - Both in % and Amount 
- **Total Amount** - Final invoice total
- **Total Paid** - Amount paid to supplier
- **Remaining** - Balance due (for credit invoices)

**Managing Purchase Invoices:**
- **View** - See complete invoice details
- **Edit** - Modify invoice (before payment completion)
- **Delete** - Remove invoice (password protected)

### Purchase Returns

**Purpose:** Record returns to suppliers for damaged/expired/incorrect items

**Creating a Purchase Return:**
1. Navigate to Purchase Returns → Add Return
2. Select original purchase invoice
3. System shows available items from that invoice
4. Select items to return with quantities
5. Specify return reason
6. Save return - automatically adjusts stock

---

## Sales Management

### Sale Invoices

**Purpose:** Record all sales to customers

**Creating a Sale Invoice:**

**Navigation:** Sale Invoices → Add Invoice (or Alt+N)

**Header Information:**
| Field | Description |
|-------|-------------|
| **Posted Number** | Auto-generated (SI-XXXXXX) |
| **Customer** | Select existing customer |
| **Date** | Sale date |
| **Invoice Type** | Cash or Credit |
| **Sale Type** | Retail or Wholesale |
| **Whole Sale Type** | Unit or pack |

**Adding Items:**
1. Search and select product
2. System shows:
   - pack size
   - Batch information
   - Expiry Date
   - Available stock
   - Pack/Unit sale price
3. Enter quantity
4. Apply item discount if needed
5. Add to invoice

**Pricing Modes:**
- **Retail** - Standard sale prices
- **Wholesale** - Special wholesale prices (if configured for customer)

**Invoice Summary:**
- Subtotal, discount, tax, total
- **Total Received** - Amount collected
- **Remaining** - Balance due (for credit sales)

**Printing Invoices:**
- **Thermal Printer** - Compact receipt format
- **A4 Printer** - Full-page invoice
- **6 Thermal Templates** - Standard, Minimal, Detailed, Compact, Bold, Barcode
- Preview templates in Settings → Printer Settings

### Sale Returns

**Purpose:** Process customer returns

**Creating a Sale Return:**
1. Select original sale invoice
2. Choose items being returned
3. Enter return quantities
4. Specify reason
5. System automatically:
   - Adds stock back to inventory
   - Updates customer balance (if credit sale)
   - Records return for reporting

## Ledgers

**Supplier Ledger:**
- Complete transaction history with each supplier
- Shows all purchases and returns
- Running balance calculation
- Filter by date range
- Export to PDF

**Customer Ledger:**
- Complete transaction history
- All sales and returns
- Payment tracking
- Outstanding balance
- Export to PDF

---

## Inventory & Stock

### Stock Adjustments

**Purpose:** Correct inventory discrepancies (damage, theft, counting errors)

**Types of Adjustments:**
- **Increase** - Add stock (found items, returns not invoiced)
- **Decrease** - Remove stock (damaged, expired, lost)

**Creating Adjustment:**
1. Select product
3. Enter quantity
4. Specify reason
5. Save - immediately updates stock levels

### Current Stock Report

**Access:** Reports → Current Stock

**Features:**
- Real-time stock levels
- Filter by category, brand, supplier
- Shows:
  - Product name
  - Brand/Supplier
  - Pack size
  - Current quantity
  - Purchase/Sale values
  - Potential profit

**Export:** PDF generation for physical records

### Near Expiry Alerts

**Dashboard Widget:**
- Shows products expiring soon
- Filter by 1, 3, 6, 12, 18 months
- Filter by supplier/brand
- Helps prevent losses from expired inventory

---

## Reports

### Available Reports

| Report | Purpose | Data Included |
|--------|---------|---------------|
| **Current Stock** | Inventory valuation | Stock levels, values, profit potential |
| **Cost of Sale** | Profitability analysis | Purchase costs vs sale prices |
| **Purchase Detail** | Purchase analysis | All purchases by date/supplier/product |
| **Sale Detail** | Sales analysis | All sales by date/customer/product |
| **Stock Adjustment** | Adjustment tracking | All stock corrections with reasons |
| **Product Comprehensive** | Product performance | Complete product transaction history |

### Using Reports

**Common Features:**
- **Date Range Filter** - Select period for analysis
- **Category/Brand/Supplier Filters** - Narrow down data
- **Export PDF** - Generate printable reports
- **Real-time Data** - Always current information

**Report Best Practices:**
1. Run reports monthly for business review
2. Export PDFs for record keeping
3. Use filters to identify trends
4. Check Cost of Sale for pricing optimization

---

## Settings & Customization

### General Settings

**Store Information:**
- **Store Name** - Displayed on invoices and reports
- **Phone Number** - Contact number
- **Address** - Store address
- **Logo** - Upload store logo (appears on invoices)
- **License Number** - Business license (optional)
- **Footer Note** - Custom message on invoices

### Theme Settings

**Navigation Style:**
- **Sidebar** - Traditional left navigation
- **Topbar** - Horizontal top navigation

**Sidebar Templates (12+ Styles):**
- Classic, Modern, Minimal, Glass, Gradient
- Cyber, Aurora, Nebula, Elegant, Vibrant
- Floating, Mini

**Topbar Templates:**
- Matching styles for top navigation

**Color Customization:**
- Primary Color - Main brand color
- Secondary Color - Accent color
- Tertiary Color - Additional accent
- Success/Warning/Danger colors
- Light/Dark mode support

**Button Styles:**
- **Rounded** - Standard rounded buttons
- **Soft** - Extra rounded, modern look
- **Outlined** - Border-only buttons

### Printer Settings

**Printer Type:**
- **Thermal** - Receipt printers (80mm)
- **A4** - Standard document printers

**Thermal Templates:**
1. **Standard** - Balanced layout
2. **Minimal** - Compact, minimal info
3. **Detailed** - Complete information
4. **Compact** - Smallest footprint
5. **Bold** - Large, readable text
6. **Barcode** - With product barcodes

**Preview:** Test templates with sample data before printing

### Backup & Restore

**Backup:**
- Create full database backup
- Download backup file
- Schedule regular backups

**Restore:**
- Upload backup file
- System restores all data
- Use with caution (replaces current data)

### License Management

**View License:**
- Current plan details
- Expiration date
- Feature availability

**Activate License:**
- Enter license key
- System validates and activates
- Unlocks full features

---

## User Management & Security

### User Management

**Creating Users:**
- Name, email, password
- Assign roles
- Set permissions

**User Roles:**
- **Admin** - Full system access
- **Manager** - Most operations, limited settings
- **Cashier** - Sales and returns only
- **Inventory** - Products and purchases only
- **Custom Roles** - Define specific permissions

### Role & Permission System

**Available Permissions:**
| Module | Permissions |
|--------|-------------|
| Products | View, Create, Update, Delete, Import, Export |
| Categories | View, Create, Update, Delete |
| Brands | View, Create, Update, Delete, Import |
| Suppliers | View, Create, Update, Delete, Import |
| Customers | View, Create, Update, Delete, Import |
| Purchase Invoices | View, Create, Update, Delete |
| Sale Invoices | View, Create, Update, Delete |
| Returns | View, Create, Update, Delete |
| Reports | View, Export |
| Settings | View, Update |
| Users | View, Create, Update, Delete |
| Roles | View, Create, Update, Delete |

### Security Features

**Password-Protected Deletions:**
- Deleting invoices requires password confirmation
- Prevents accidental data loss
- Audit trail of deletions

**Session Management:**
- Secure authentication
- Password reset functionality
- Role-based access control

---

## Keyboard Shortcuts

### Global Shortcuts

| Shortcut | Action | Context |
|----------|--------|---------|
| **Alt + N** | Create New | List pages (Products, Invoices, etc.) |
| **Alt + S** | Save | Form pages (Settings, Product Edit, etc.) |
| **Alt + R** | Refresh | Dashboard, Reports |

### Navigation Shortcuts

| Key | Action |
|-----|--------|
| **Arrow Down/Up** | Navigate menu items |
| **Enter** | Select menu item |
| **Home** | First menu item |
| **End** | Last menu item |

### Form Shortcuts

| Shortcut | Action |
|----------|--------|
| **Tab** | Next field |
| **Shift + Tab** | Previous field |
| **Enter** | Submit/Save (in forms) |

---

## Troubleshooting

### Common Issues

**Cannot Delete Product:**
- Product has stock quantity > 0
- Product has batch records
- Solution: Clear stock or remove batches first

**Invoice Won't Print:**
- Check printer settings
- Verify printer type (Thermal/A4)
- Try different thermal template
- Check browser popup blocker

**Stock Not Updating:**
- Verify batch information is correct
- Check for pending adjustments
- Review purchase/sale invoice status

**Report Shows No Data:**
- Check date range filters
- Verify category/brand filters are correct
- Ensure data exists for selected period

**Login Issues:**
- Verify correct subdomain URL
- Check email/password
- Use "Forgot Password" if needed
- Clear browser cache

### Best Practices

1. **Regular Backups** - Create backups weekly
2. **Stock Counts** - Physical inventory monthly
3. **Expiry Checks** - Review near-expiry weekly
4. **User Training** - Train staff on their specific roles
5. **Password Security** - Use strong passwords, change periodically
6. **Data Validation** - Double-check entries before saving

### Support

For technical support or feature requests, contact your system administrator or SaaS provider.

---

## Quick Reference Cards

### Product Entry Checklist
- [ ] Product code unique
- [ ] Name descriptive
- [ ] Category selected
- [ ] Brand selected
- [ ] Pack size correct
- [ ] Pricing accurate
- [ ] Barcode unique
- [ ] Image uploaded (optional)

### Invoice Creation Checklist
- [ ] Supplier/Customer selected
- [ ] Date correct
- [ ] Items added with quantities
- [ ] Batch numbers recorded
- [ ] Expiry dates entered
- [ ] Totals verified
- [ ] Payment recorded (if applicable)

### End of Day Checklist
- [ ] All sales recorded
- [ ] Stock adjustments made
- [ ] Backup created
- [ ] Reports reviewed
- [ ] Expiring items noted

---

**Document Version:** 1.0  
**Last Updated:** 2025  
**Application:** Karobar App - Business Management System
