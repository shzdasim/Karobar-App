# Multi-PC LAN Terminals & Cube AI Assistant — User Manual (English)

> **Who uses this screen?** Store Owners & Admins (Multi-PC Server/Terminals) and All Staff (Cube AI Assistant).  
> **Where to find it:** **Server Dashboard / Terminals** (`/server-dashboard`), **Settings → Server & Terminals / Cube AI**, and the **Cube AI Assistant** icon in the Topbar.

---

## A. Purpose and Overview

- **Multi-PC LAN Terminals (`/server-dashboard`):** Lets larger pharmacies run multiple billing counters simultaneously on the same shop Wi-Fi or Ethernet network—one PC acts as the **Server** holding the main database, while other PCs connect as **Terminals** and stay synchronized in real time.
- **Cube Hybrid AI Assistant:** Your built-in bilingual (**English + Urdu / Roman Urdu**) helper that explains any screen or button, answers *"how do I..."* questions, and helps with safe catalog lookups.

---

## B. What Users Can Do

### Multi-PC LAN Terminals (`/server-dashboard`)
- View **Connected Terminals**, last ping/heartbeat time, and online/offline status.
- Run **Diagnostics** & **Troubleshoot** if a counter terminal cannot reach the server.
- Export/sync terminal configuration or manage blocked/stale terminals.

### Cube AI Assistant (Topbar)
- Ask questions in **English, Urdu, or Roman Urdu** (for example: *"How do I return an expired medicine?"* or *"Panadol ka batch kaise check karein?"*).
- Use **Cursor Guide Mode** to point at any button/field on screen and get an instant plain-language explanation.

---

## C. Screen-by-Screen Explanation

*(No standalone screenshot is currently stored for `/server-dashboard` or Cube AI in `Screenshots/`.)*

1. **Server Dashboard (`/server-dashboard`):** Server IP/Port banner, Connected Terminals table, and **Diagnostics / Troubleshoot** buttons.
2. **Cube AI Drawer (Topbar Icon):** Chat window with bilingual suggestions, Guide Mode toggle, and Background Tasks status (`/background-tasks`).

---

## D. Every Field, Button, Tab and Option

| Element | Feature | What It Does |
| :--- | :--- | :--- |
| **Connected Terminals Table** | LAN Sync | Shows each counter PC name, IP, last seen time, and Active/Blocked status. |
| **Diagnostics / Troubleshoot** | LAN Sync | Checks network port, firewall, and database availability. |
| **Cube AI Chat & Guide Bot** | Cube AI | Answers ERP usage questions and explains on-screen buttons in English/Urdu. |

---

## E. Step-by-Step Instructions

### Checking Connected Counter Terminals
1. On the main Server PC, open `/server-dashboard` (or **Settings → Server & Terminals**).
2. Confirm all counter PCs show **Online** with a recent heartbeat.
3. If a terminal is offline, click **Troubleshoot** and verify both PCs are on the same shop Wi-Fi/LAN.

---

## F. Practical Business Examples

### Example: Two Counters Serving Rush-Hour Patients
Counter 1 (Main Server) and Counter 2 (LAN Terminal) bill patients simultaneously. When Counter 1 sells the last 2 packs of *Augmentin*, Counter 2 sees the updated `0` stock immediately so two cashiers never sell the same last box.

---

## G. Expected Results

- Multi-PC counters share live stock, invoices, and ledgers; Cube AI provides instant in-app help without calling support.

---

## H. Common Mistakes and Solutions

| Mistake | Why It Happens | Solution |
| :--- | :--- | :--- |
| **Counter Terminal cannot connect after router restart** | Router assigned a new IP address to the Server PC. | Check the Server PC's current IP on `/server-dashboard` and update the Terminal connection URL. |

---

## I. Related Features and Workflows

- [Store, Theme & License Settings](./settings.md)
- [Troubleshooting Guide](../../customer/en/TROUBLESHOOTING.md)
