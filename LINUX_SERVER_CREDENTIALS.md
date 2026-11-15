 # Karobar-App ERP v2 — Linux Store Server Deployment & Credentials

**Deployed At:** 2026-11-15  
**Server IP:** `185.165.154.41`  
**Application Port:** `8000`  
**Install Directory:** `/opt/karobar-server`

---

## 1. Web URLs & Quick Access

| Resource | URL |
| :--- | :--- |
| **Main ERP & POS Application** | http://185.165.154.41:8000 |
| **Server & Attached Terminals Dashboard** | http://185.165.154.41:8000/server-dashboard |
| **1-Click Instant Admin Launch (Auto-Login)** | http://185.165.154.41:8000/server-launch?key=2307316605ba60240c55cbbd3ca053919b27460234230e90 |
| **Terminal Handshake / Ping Endpoint** | http://185.165.154.41:8000/api/terminals/ping |
| **Interactive Architecture & Setup Guide** | http://185.165.154.41:8000/server-guide.html |

---

## 2. Predefined Application Logins (ERP & Server Dashboard)

Both accounts have the **Admin** role (`sanctum` guard) with full permissions across all modules, reports, and server management:

| Account | Email | Password | Role |
| :--- | :--- | :--- | :--- |
| **Primary Store Admin** | `admin@karobar.local` | `admin12345` | `Admin` |
| **Secondary Admin** | `admin@example.com` | `password` | `Admin` |

---

## 3. Multi-PC POS Terminal Connection Details

Use these settings when installing the Windows `.exe` in **Multi-PC POS Terminal Mode** or in **Settings → Server & Terminals**:

- **Server URL:** `http://185.165.154.41:8000`
- **Network Server Token:** `KRB-NET-HWGYIO0VPQ3P`
- **Auto-Authorize New Terminals:** `Enabled` (`true`)
- **Launch Key (`storage/app/server-launch.key`):** `2307316605ba60240c55cbbd3ca053919b27460234230e90`

---

## 4. Hardware Machine ID & Activated License

- **Hardware Machine ID:** `KRB-F7FC-9014-C957-7DD3`
- **Plan:** `Enterprise Multi-PC` (10-Year Validity)
- **Activated License Key:**
  ```text
  KRBL.Lb-MXpAnuLjxwz8kMIV1P96oibR-tge6C7TILKlrpuy_L_klZRFxaby6-7YS6OPxWB9cBJnA2axKsMcmAABdDA.eyJpc3MiOiJLYXJvYmFyIiwic3ViIjoiS2Fyb2JhckFwcCIsIm1hY2hpbmUiOiJLUkItRjdGQy05MDE0LUM5NTctN0REMyIsImN1c3RvbWVyIjoiS2Fyb2JhciBTdG9yZSBTZXJ2ZXIiLCJwbGFuIjoiRW50ZXJwcmlzZSBNdWx0aS1QQyIsImlhdCI6MTc5NDc0MzE5NCwiZXhwIjoyMTEwMTAzMTk0fQ
  ```

---

## 5. Linux Server SSH Access

- **Host / IP:** `185.165.154.41`
- **Port:** `22`
- **User:** `root`
- **Password:** `yj1pgCWmfjVwDO6BycS7`
- **OS:** Ubuntu 22.04.5 LTS (`x86_64`, 16 GB RAM, 94 GB Disk)

```bash
ssh root@185.165.154.41 -p 22
```

---

## 6. Installed Services & Server Management (`karobar-ctl`)

- **Web Server:** Nginx (`/etc/nginx/sites-available/karobar.conf`)
- **PHP Worker Pool:** PHP `8.4.27` FPM (`32` dynamic workers, `/etc/php/8.4/fpm/pool.d/karobar.conf`)
- **Database:** MariaDB (`karobar_erp`, InnoDB row-level locking, `/etc/mysql/mariadb.conf.d/99-karobar-innodb.cnf`)
- **Cache & Real-Time SyncPulse:** Redis (`127.0.0.1:6379`)
- **Background Daemons:** `karobar-scheduler.service`, `karobar-whatsapp.service`

### Useful CLI Commands on the Server

```bash
# Check service status & URLs
sudo karobar-ctl status

# List all registered/connected POS terminals
sudo karobar-ctl terminals

# Import a Standalone SQLite database into MariaDB
sudo karobar-ctl import-sqlite /path/to/database.sqlite --mode=replace

# Export MariaDB into a portable .sqlite file for 1-PC Standalone mode
sudo karobar-ctl export-sqlite /tmp/karobar-portable.sqlite

# Create an immediate database backup
sudo karobar-ctl backup

# Restart all server services (MariaDB, Redis, PHP-FPM, Nginx, Scheduler, WhatsApp)
sudo karobar-ctl restart

# View live Laravel application logs
sudo karobar-ctl logs

# Print the server's hardware Machine ID
sudo karobar-ctl machine-id
```
