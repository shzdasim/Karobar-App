# Users & Roles (Staff Access Control) — User Manual (English)

> **Who uses this screen?** Pharmacy Owners and System Administrators.  
> **Where to find it:** Sidebar → **System & Settings → Users** (`/users`) and **Roles** (`/roles`).

---

## A. Purpose and Overview

The **Users & Roles** modules protect your pharmacy's sensitive data and money. Every employee gets their own login (**Users**) linked to a **Role** (such as *Cashier*, *Pharmacist*, *Inventory Manager*, or *Accountant*) that controls exactly which screens and buttons they can access.

---

## B. What Users Can Do

- **Users (`/users`):** Create staff logins, assign Roles, set active/inactive status, set/clear 4-digit **Operator PINs**, and reset staff passwords.
- **Roles (`/roles`):** Create custom roles and toggle granular permissions (View, Create, Edit, Delete, Export, Print, Discount Override, Expired Override, Stocktake Finalize).

---

## C. Screen-by-Screen Explanation

### 1. Users (`/users`)

![Users Page](../../../Screenshots/Users-Page.webp)

### 2. Roles (`/roles`)

![Roles Page](../../../Screenshots/Roles-Page.webp)

1. **Users List (`/users`):** Search staff, **Add User**, Edit user/role/PIN, or deactivate an employee who left.
2. **Roles & Permissions Matrix (`/roles`):** List of roles; editing a role opens a grouped checklist of permissions per module.

---

## D. Every Field, Button, Tab and Option

| Element | Screen | What It Does |
| :--- | :--- | :--- |
| **Add User / Edit User** | Users | Sets Name, Email, Password, Role(s), Active status, and optional 4-digit Operator PIN. |
| **Create Role / Permission Checkboxes** | Roles | Controls View, Create, Update, Delete, Export, and Override rights per module. |

---

## E. Step-by-Step Instructions

### Creating a Restricted Cashier Account
1. Go to **Roles → Create Role** (`/roles/create`), name it *Counter Cashier*, check only `barcode-pos.view`, `barcode-pos.create`, `sale-invoice.view`, `sale-invoice.create`, `cash-drawer.view`, and `user-demands.create`, and click **Save**.
2. Go to **Users → Add User** (`/users/create`), enter the cashier's name, email, and password, assign the **Counter Cashier** role, and click **Save**.

---

## F. Practical Business Examples

### Example: Preventing Unauthorized Deletions
By keeping `sale-invoice.delete`, `purchase-invoice.delete`, `stock-adjustment.create`, and `report.net-profit.view` unchecked on the *Counter Cashier* role, cashiers can bill quickly all day without seeing owner profit reports or deleting past bills.

---

## G. Expected Results

- On next login, the employee's sidebar and buttons automatically show only the modules permitted by their role.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Cannot delete an employee who left** | They have past Sale Invoices or Cash Drawer shifts linked for audit history. | **Deactivate** (set inactive) the user instead of deleting so historical bills keep their cashier name. |

---

## I. Related Features and Workflows

- [Login, Setup Wizard & Profile](./auth-setup-profile.md)
- [Owner & Manager Guide](../../customer/en/OWNER_GUIDE.md)
