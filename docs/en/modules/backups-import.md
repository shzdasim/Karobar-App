# Backups & Data Import — User Manual (English)

> **Who uses this screen?** Pharmacy Owners and System Administrators.  
> **Where to find it:** Sidebar → **System & Settings → Backups & Storage** (`/backups`) and **SQLite Data Import** (`/settings/sqlite-import`).

---

## A. Purpose and Overview

The **Backups & Data Import** modules protect your pharmacy from hard-drive crashes or accidental mistakes by creating automated/manual backups of your store database, and let administrators migrate data from an existing store database.

---

## B. What Users Can Do

- **Create Manual Backups** anytime in `/backups`.
- **Automated Schedule:** Configure hourly/daily automatic backups and custom backup storage folders (for example, syncing to a D: drive or cloud folder).
- **Download, Upload, or Restore a Backup** (protected by password confirmation—and Karobar-App automatically saves a safety backup before restoring).
- **CSV & SQLite Data Import:** Bulk-import Products, Categories, Brands, Customers, or Suppliers from CSV, or migrate a legacy SQLite database (`/settings/sqlite-import`, Admin-only).

---

## C. Screen-by-Screen Explanation

![Backups & Storage](../../../Screenshots/Backup-Storage.webp)

1. **Backups List & Actions:** **Create Backup**, **Upload Backup**, and **Run Scheduled Job Now**.
2. **Automated Schedule Tab:** Enable/disable automatic backups, set retention count, and choose backup folder.
3. **Backup History Table:** Date, File Size, Type, Download, **Restore** (password-protected), and Delete actions.

---

## D. Every Field, Button, Tab and Option

| Element | Type | What It Does |
| :--- | :--- | :--- |
| **Create Backup** | Button | Immediately saves a full snapshot of your store data. |
| **Automated Schedule** | Tab | Turns on periodic auto-backups and sets how many old backups to keep. |
| **Upload & Restore** | Buttons | Restores a saved backup file after verifying your Admin password. |

---

## E. Step-by-Step Instructions

### Creating & Saving an Off-PC Backup
1. Go to **System & Settings → Backups & Storage** (`/backups`).
2. Click **Create Backup**.
3. Click **Download** on the new backup row and copy the file to a USB drive or Google Drive folder for safekeeping.

---

## F. Practical Business Examples

### Example: Recovering from an Accidental Bulk Edit
If someone accidentally overwrites prices on 50 items, go to **Backups & Storage**, pick yesterday's backup, enter your Admin password, and click **Restore**—your store returns cleanly to yesterday's state.

---

## G. Expected Results

- Manual and scheduled backups appear in the Backup History list; restoring replaces active store data after creating a pre-restore safety snapshot.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Restore fails with password error** | Incorrect Admin password entered in the Restore confirmation modal. | Re-enter your current Admin login password. |

---

## I. Related Features and Workflows

- [Store, Theme & License Settings](./settings.md)
- [Owner & Manager Guide](../../customer/en/OWNER_GUIDE.md)
