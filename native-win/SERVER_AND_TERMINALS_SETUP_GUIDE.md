# Karobar-App Native — Server & Multi-PC Terminals Setup Guide

This guide explains how **Server & Terminals (Multi-PC LAN Mode)** works under the hood in **Karobar-App Native (.NET 9 + Embedded Kestrel + Multi-Tab WebView2)** and gives you a step-by-step checklist for deploying a rock-solid multi-counter store/pharmacy network on Windows.

---

## 1. Architecture Overview (How It Works)

Karobar-App Native uses a **zero-config Store Server + Thin Native Terminal** architecture over your shop's Local Area Network (LAN / Wi-Fi / Ethernet):

```
                ┌──────────────────────────────────────────────────────┐
                │          STORE SERVER PC (Standalone Mode)           │
                │  IP: 192.168.1.10 (DHCP Reserved) · TCP Port 18960   │
                │                                                      │
                │  • Karobar-Native.exe (Kestrel bound to 0.0.0.0:18960)│
                │  • Single Authoritative SQLite 3 Database (WAL Mode) │
                │  • Atomic Document Sequences (SI / SLRET / PRINV)    │
                │  • 4-Second Real-Time SyncPulse Broadcast Hub        │
                │  • Windows Firewall: TCP 18960 (remoteip=localsubnet)│
                │  • Can ALSO be used as Counter 1 (full POS + UI!)    │
                └──────────────────────────┬───────────────────────────┘
                                           │  Local LAN (Ethernet / Wi-Fi)
                 ┌─────────────────────────┼─────────────────────────┐
                 ▼                         ▼                         ▼
    ┌────────────────────────┐ ┌────────────────────────┐ ┌────────────────────────┐
    │   COUNTER 2 PC         │ │   COUNTER 3 PC         │ │   MANAGER / BACKOFFICE │
    │   (Terminal Mode)      │ │   (Terminal Mode)      │ │   (Terminal Mode)      │
    │                        │ │                        │ │                        │
    │ • Connects to Server   │ │ • Connects to Server   │ │ • Connects to Server   │
    │   http://192.168.1.10  │ │   http://192.168.1.10  │ │   http://192.168.1.10  │
    │   :18960               │ │   :18960               │ │   :18960               │
    │ • Local SQLite NOT used│ │ • Local SQLite NOT used│ │ • Local SQLite NOT used│
    │ • Own Thermal Printer  │ │ • Own Thermal Printer  │ │ • Own A4 / Laser Print │
    │ • Own Cash Drawer Kick │ │ • Own Cash Drawer Kick │ │ • Full Reports / Stock │
    │ • Own Barcode Scanner  │ │ • Own Barcode Scanner  │ │ • GuideBot + Mascot    │
    │ • Auto-reconnects in   │ │ • Auto-reconnects in   │ │ • Separate User Login  │
    │   10s if Server reboots│ │   10s if Server reboots│ │   & RBAC Permissions   │
    └────────────────────────┘ └────────────────────────┘ └────────────────────────┘
```

### What Runs Where?

| Component | Store Server PC | Counter Terminal PC |
| :--- | :--- | :--- |
| **Database (`database.sqlite` + WAL)** | Authoritative single database (WAL + Private Cache + 15s busy timeout + auto-retry) | Not started — reads/writes directly to Store Server over LAN |
| **Invoice Numbering (`SI-`, `PRINV-`)** | Atomic server-side sequence allocator (zero duplicate invoice numbers) | Allocated by Store Server |
| **Real-Time Sync (`SyncPulse`)** | Bumps `stock`, `drafts`, `drawer`, `terminals`, `settings` versions on every write | Leader-elected tab polls `/api/sync/pulse` every 4s and auto-refreshes stock, held bills, and drawers |
| **License Key** | Hardware-bound Ed25519 license installed **only on Store Server** (controls `seats` limit) | No separate license key required (shares Store Server's seat pool) |
| **Thermal Printer & Cash Drawer** | Local printers attached to Server PC | **Local printers & cash drawer attached to each Counter PC** (`window.karobarDesktop` prints locally!) |
| **Native GuideBot & Roaming Mascot** | Runs in-process against local DB & AI keys | Runs natively in the Terminal window and routes AI/Tasks over authenticated LAN HTTP to the Store Server |
| **Cashier Login & Cash Drawer Session** | Independent cashier login & drawer session | Independent cashier login & per-terminal cash drawer session |

---

## 2. Network Preparation (Do This Once — Prevents 95% of Issues)

1. **Same Router / Subnet:** Connect the Store Server PC and all Counter Terminal PCs to the **same router or Ethernet switch** (e.g., all on `192.168.1.x`).
   - *Best practice:* Connect the **Store Server PC via Ethernet cable** instead of Wi-Fi for rock-solid sub-2ms latency and zero Wi-Fi sleep drops.
2. **Reserve a Static LAN IP for the Store Server PC (Crucial!):**
   - If your router reassigns the Store Server's IP after a power outage (e.g., `192.168.1.10` → `192.168.1.15`), terminals won't know the new IP until updated.
   - **Recommended:** Log into your shop router (`192.168.1.1` or `192.168.0.1`) → **DHCP Address Reservation / Static Lease** → bind the Store Server PC's MAC address permanently to a fixed IP like **`192.168.1.10`**.
3. **Disable Sleep on the Store Server PC:**
   - Windows **Settings → System → Power & battery → Screen and sleep** → set *"Put my device to sleep"* to **Never** (when plugged in).
   - If the Store Server goes to sleep, Counter PCs will lose connection until it wakes up.

---

## 3. Step-by-Step: Setting Up the Store Server PC

1. Run **`Karobar-Native-Setup.exe`** on your main Server PC.
2. On the **Deployment Mode** page, choose one of the Standalone options:
   - **Fresh Database** (new store), **Import Existing SQLite**, or **Keep Existing Database** (in-place upgrade).
   - *Note:* In Karobar-App Native, every Standalone installation **automatically acts as a LAN Store Server** listening on `0.0.0.0:18960`.
3. Complete License Key binding and Admin account creation.
4. During installation, Setup automatically configures Windows Defender Firewall:
   ```powershell
   netsh advfirewall firewall add rule name="Karobar-App Native LAN Server (TCP 18960)" dir=in action=allow protocol=TCP localport=18960 remoteip=localsubnet profile=any
   ```
   - This works automatically whether Windows sets your shop network to **Private**, **Domain**, or **Public**, while strictly blocking traffic from outside your local LAN (`localsubnet`).
5. Open **Karobar-App Native** on the Server PC, sign in as Admin, and go to **Settings → Server & Terminals**.
6. Note two values from the top summary cards:
   - **Terminal Connection URL (LAN IP):** e.g. `http://192.168.1.10:18960`
   - **Server Pairing Token:** e.g. `KRB-NET-A1B2C3D4E5F6`
7. Click **Run Deep Diagnostics** under the **Troubleshooter & Logs** tab and verify all **6 health cards** are green (`Database Engine`, `SyncPulse`, `Document Sequences`, `Disk Space`, `LAN Network`, and `Windows Firewall TCP 18960`).

### Optional: Running the Store Server Headless (Dedicated Server PC)

If you have a dedicated back-office PC or mini-PC that nobody sits at, you can run the server without a GUI window:
```powershell
"C:\Program Files\Karobar-App-Native\Karobar-Native.exe" --server-only
```
(You can add this to Windows Task Scheduler **At startup** so the Store Server starts automatically whenever the PC boots after a power outage.)

---

## 4. Step-by-Step: Setting Up Counter Terminal PCs (3 Easy Methods)

You do **not** need a separate license key or admin setup on Counter Terminal PCs. Pick whichever setup method is fastest for you:

### Method A — During Installation (Recommended for New Counter PCs)

1. Run **`Karobar-Native-Setup.exe`** on Counter 2 PC.
2. On the **Deployment Mode** page, select:
   **`Multi-PC POS Terminal: Connect to Central Store Server over LAN`**
3. Enter:
   - **Store Server LAN URL:** `http://192.168.1.10:18960` (copied from Server PC)
   - **Terminal / Counter Name:** e.g. `Counter 2 — Pharmacy`
   - **Optional Server Pairing Token:** paste `KRB-NET-...` from the Server PC (pre-registers the counter immediately on first ping!)
4. Click **Next → Install**. License & Admin creation pages are automatically skipped.
5. Launch **Karobar-App Native** — it connects directly to the Store Server login screen!

### Method B — In-App Mode Switcher (Convert an Existing PC Anytime)

1. Open **Karobar-App Native** on the Counter PC.
2. Go to **Settings → Server & Terminals** and scroll to **This Windows PC — Operating Mode**.
3. Switch **PC Operating Mode** to **`Multi-PC POS Terminal (Connect to Store Server)`**.
4. Enter the **Store Server LAN URL**, **Counter Name**, and optional **Pairing Token**.
5. Click **Test Server Connection** → confirm green latency check (typically `1–5 ms`).
6. Click **Save & Apply PC Mode** — the app stops its local Kestrel instance, closes old tabs, and connects cleanly to the Store Server.

### Method C — 1-Click Pre-Configured JSON File (Fastest for 3+ Counters)

1. On the **Store Server PC**, go to **Settings → Server & Terminals**.
2. Under **Connect a New POS Counter**, type `Counter 3 — Dispensing` and click **Download Terminal Config (.json)**.
3. Copy that downloaded file onto the Counter 3 PC at:
   ```text
   %APPDATA%\Karobar-App-Native\network-config.json
   ```
   *(Press `Win + R`, type `%APPDATA%\Karobar-App-Native`, press Enter, and paste/rename the file to `network-config.json`.)*
4. Launch **Karobar-App Native** on Counter 3 — it automatically reads the Server URL, Terminal ID, Counter Name, and Pairing Token on boot.

---

## 5. Security, Pairing Tokens, Authorization & License Seats

- **Terminal Auto-Authorize (Default: ON):**
  - On the Store Server (**Settings → Server & Terminals**), when **`Auto-authorize new LAN terminals`** is checked, any PC on your LAN that signs in with a valid Karobar user account (or presents the Pairing Token) is automatically registered and authorized — up to your license seat limit.
- **Strict Approval Mode (Uncheck Auto-Authorize):**
  - Uncheck **`Auto-authorize new LAN terminals`** if you want manual administrator approval before any new PC can access the store.
  - New terminals appear in the **Attached POS Terminals** table as **Blocked / Pending Approval**.
  - Until you click **Authorize**, that terminal sees a full-screen **"Terminal Authorization Required"** screen showing its Terminal UID, with **Retry Connection** and **Switch to Standalone Mode** buttons.
- **Instant Remote Block / Revoke:**
  - Click **Block** next to any counter in **Settings → Server & Terminals** to immediately revoke its API access (`HTTP 403 TERMINAL_UNAUTHORIZED`) without restarting the server.
- **Pairing Token Rotation:**
  - Click **Rotate Token** anytime to invalidate the old `KRB-NET-...` token. Already-authorized terminals stay authorized; new unauthenticated terminals will need the new token (or a valid cashier login).
- **Per-Cashier Accountability & Drawers:**
  - Create a separate user account for each cashier under **Users & Roles** on the Server PC.
  - Each counter tracks its own **Active Cashier**, **Open Cash Drawer Session**, and **Today's Invoice Count / Sales Total** independently in the Server dashboard.

---

## 6. Local Printers, Cash Drawers & Barcode Scanners on Terminals

Even though Terminal PCs load data from the Store Server over LAN, **hardware stays 100% local to each Counter PC**:

- **Thermal Receipt Printer:** Go to **Settings → Printer** on Counter 2 and select Counter 2's USB/LAN thermal printer. Preferred printer settings are saved locally in `%APPDATA%\Karobar-App-Native\printer-config.json` on each PC, so Counter 1 prints to Counter 1's printer and Counter 2 prints to Counter 2's printer.
- **Cash Drawer Kick:** ESC/POS drawer kick commands run via Win32 raw spooler directly on the Counter PC where the cashier pressed Save/Print.
- **Barcode Scanners:** USB/Bluetooth HID scanners type directly into the Counter PC's active WebView2 POS tab.

---

## 7. Handling Server Reboots, Power Outages & OTA Updates

### What Happens If the Store Server Reboots?
- If a Counter PC launches while the Store Server is still booting (or unreachable), Karobar-App Native automatically opens the built-in **Terminal Disconnected Recovery Screen**.
- It **automatically probes the Store Server every 10 seconds** (pauses if you start typing) and reconnects the moment the Store Server comes back online — zero manual steps needed!
- You can also edit the Server IP, test the connection, or switch back to Standalone Mode right from that screen.

### How to Apply OTA App Updates Across a Multi-PC Store
1. **Update the Store Server PC first:** Click **Install Update** on the Store Server PC. It backs up the SQLite DB, applies the differential patch (`Karobar-Native-Patch.zip`) or installer, runs schema upgrades, and relaunches on port `18960`.
2. **Counter PCs:** Since Counter PCs serve the React UI directly from the Store Server, they get all UI/API updates immediately after refresh! Update the Counter PC's desktop binary (`Karobar-Native.exe`) via the update prompt whenever a native shell update is released.

---

## 8. Troubleshooting Matrix

| Symptom | Root Cause | Fix |
| :--- | :--- | :--- |
| **"Cannot Reach Karobar Store Server"** on Counter 2 | Store Server PC is closed/asleep, IP changed, or Windows Firewall is blocking TCP `18960` | 1. Confirm Karobar-App Native is open on the Server PC.<br>2. Check the current LAN IP on Server → **Settings → Server & Terminals**.<br>3. Check the **Windows Firewall (TCP 18960)** card in **Run Deep Diagnostics**. If yellow/red, run in elevated PowerShell on Server PC:<br>`netsh advfirewall firewall add rule name="Karobar-App Native LAN Server (TCP 18960)" dir=in action=allow protocol=TCP localport=18960 remoteip=localsubnet profile=any` |
| **Server URL shows wrong IP (e.g. `172.x` or `192.168.56.1`)** | Virtual adapter (WSL, VirtualBox, VPN) | Karobar-App Native automatically filters virtual adapters and prioritizes physical Ethernet/Wi-Fi with a default gateway. If multiple LAN cards exist, run `ipconfig` on the Server PC and use the Ethernet/Wi-Fi IPv4 address. |
| **Terminal shows "Pending Approval / Blocked"** | Auto-authorize is disabled, seat limit reached, or admin clicked Block | On Server PC → **Settings → Server & Terminals** → click **Authorize** next to the terminal (or prune old offline ghost terminals if license seats are full). |
| **Stock or Held Bills take a moment to refresh on Counter 2** | Normal SyncPulse cycle is 4 seconds | Click **Force Sync All Counters** in **Settings → Server & Terminals** for instant broadcast, or verify the green online dot in the top bar. |
| **Port 18960 warning in Diagnostics** | Another app was using TCP `18960` when Server booted | Close the conflicting process and restart Karobar-App Native on the Server PC (or point terminals to the fallback port shown in Diagnostics). |
| **Need to move Store Server to a brand-new PC** | Hardware upgrade / replacement | On old Server → **Settings → Server & Terminals → 1-Click DB Export → Export .SQLITE**. Install Karobar-App Native on new PC (Import SQLite mode), activate license, reserve IP, and update terminal URLs if IP changed. |

---

## 9. Config File Paths Reference (Windows)

- **Network Mode & Pairing Config:**
  - `%APPDATA%\Karobar-App-Native\network-mode.json`
  - `%APPDATA%\Karobar-App-Native\network-config.json`
  - `<InstallDir>\storage\app\network-mode.json` *(most recently modified file wins)*
- **Local Preferred Printer:** `%APPDATA%\Karobar-App-Native\printer-config.json`
- **Native Mascot & GuideBot Preferences:** `%APPDATA%\Karobar-App-Native\mascot-prefs.json`
- **Authoritative Store Database (Server PC only):** `<InstallDir>\database\database.sqlite` (+ `-wal` & `-shm` files while running)
- **Startup & Runtime Logs:** `<InstallDir>\storage\logs\native-startup.log` and `%APPDATA%\Karobar-App-Native\startup-error.log`
