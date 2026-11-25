# Login, Setup Wizard & User Profile — User Manual (English)

> **Who uses this screen?** All Store Staff (Login & Profile) and Pharmacy Owners / Admins (7-Step Setup Wizard).  
> **Where to find it:** Login (`/`), Forgot Password (`/forgot-password`), Setup Wizard (`/setup`), and User Profile (`/profile`).

---

## A. Purpose and Overview

This module is your entry point into Karobar-App. It protects your store data with individual user logins, guides new store owners through a 7-step **Setup Wizard** on first launch, and lets staff update their profile or password anytime.

---

## B. What Users Can Do

- **Sign In** with email/username and password; toggle **Remember me (24h)** (unchecked sessions expire sooner for shared counter safety).
- **Reset a Forgotten Password** via email (`/forgot-password`, if SMTP email is configured).
- **Complete the 7-Step Setup Wizard (`/setup`):** Configure store name, address, currency, sale system, printer, and initial defaults on first login.
- **Update Profile (`/profile`):** Change display name, email, or password.

---

## C. Screen-by-Screen Explanation

*(Store settings preview: [General Settings](../../../Screenshots/General-Settings.webp))*

1. **Login Screen (`/`):** Email/Username field, Password field, **Remember me (24h)** checkbox, **Sign In** button, and **Forgot Password?** link.
2. **7-Step Setup Wizard (`/setup`):** Step progress bar (`Step 1 of 7` through `Step 7 of 7`) with **Back** and **Next / Finish** buttons.
3. **Profile Page (`/profile`):** Personal details form and Change Password section.

---

## D. Every Field, Button, Tab and Option

| Element | Screen | What You Enter / What Happens |
| :--- | :--- | :--- |
| **Email / Password** | Login | Personal staff credentials created by your Store Admin. |
| **Remember me (24h)** | Login | Keeps your session active for 24 hours; uncheck on shared counter PCs. |
| **Setup Steps (1–7)** | Setup Wizard | Guides you through Store Info, Currency, Sale Mode, Printer, and initial preferences. |
| **Update Profile / Password** | Profile | Saves your updated name or new password. |

---

## E. Step-by-Step Instructions

1. Open Karobar-App and enter your **Email** and **Password**.
2. Leave **Remember me (24h)** checked on your personal PC, or uncheck it on a shared cashier terminal.
3. Click **Sign In**. On first-ever launch, follow Steps 1–7 of the **Setup Wizard** and click **Finish**.

---

## F. Practical Business Examples

### Example: Safe Shared Counter Shift Handover
Cashier A logs in without **Remember me** checked and logs out at 2:00 PM. Cashier B logs in with their own account so every sale and Cash Drawer shift is tracked under the right staff member.

---

## G. Expected Results

- After login, you land on the **Dashboard** (`/dashboard`) with only the menu items allowed by your Role.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Forgot Password email not arriving** | Store SMTP Email settings are not configured yet. | Ask your Store **Admin** to reset your password in **Users** (`/users`). |
| **Session timed out mid-shift** | Idle timeout expired. | Re-enter your password in the inline session prompt—your open screen stays intact. |

---

## I. Related Features and Workflows

- [Users & Roles](./users-roles.md)
- [Store, Theme & License Settings](./settings.md)
- [Getting Started](../../customer/en/GETTING_STARTED.md)
