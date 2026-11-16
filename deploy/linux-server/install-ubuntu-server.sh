#!/usr/bin/env bash
# ==============================================================================
# KAROBAR-APP ERP v2 — SINGLE-FILE TURNKEY LINUX SERVER INSTALLER
# ==============================================================================
# Usage:
#   sudo bash karobar-server-setup.sh
#
# Optional Environment Overrides:
#   KAROBAR_PORT=8000
#   KAROBAR_IMPORT_SQLITE=/path/to/existing-database.sqlite
#   KAROBAR_ADMIN_EMAIL=admin@karobar.local
#   KAROBAR_ADMIN_PASS=<randomly-generated-if-omitted>
#
# What this SINGLE file does end-to-end:
#   1. Installs Nginx, MariaDB 10.11 LTS (InnoDB), Redis 7, PHP 8.3-FPM,
#      Node.js 20 LTS, Avahi mDNS (.local discovery), and UFW firewall rules.
#   2. Embeds and writes all production configs (MariaDB InnoDB tuning,
#      PHP-FPM 32-worker pool, Nginx LAN server, Systemd daemons, karobar-ctl).
#   3. Builds & migrates the database, seeds Roles/Permissions + Admin account,
#      and optionally imports an existing .sqlite file into MariaDB.
#   4. Creates a "Karobar Server Control Center" icon on the Linux Desktop.
#   5. Automatically launches the browser directly into the full-featured
#      Server & Attached Terminals Dashboard (/server-dashboard) to connect
#      terminals or troubleshoot!
# ==============================================================================

set -euo pipefail

APP_DIR="${KAROBAR_APP_DIR:-/opt/karobar-server}"
SERVER_PORT="${KAROBAR_PORT:-8000}"
DB_NAME="${KAROBAR_DB_NAME:-karobar_erp}"
DB_USER="${KAROBAR_DB_USER:-karobar}"
DB_PASS="${KAROBAR_DB_PASS:-$(openssl rand -hex 16)}"
ADMIN_NAME="${KAROBAR_ADMIN_NAME:-Store Administrator}"
ADMIN_EMAIL="${KAROBAR_ADMIN_EMAIL:-admin@karobar.local}"
ADMIN_PASS="${KAROBAR_ADMIN_PASS:-$(openssl rand -base64 18 | tr -dc 'A-Za-z0-9!@#%^*' | head -c 16)}"
IMPORT_SQLITE="${KAROBAR_IMPORT_SQLITE:-}"
GIT_REPO_URL="${KAROBAR_GIT_URL:-}"

if [[ $EUID -ne 0 ]]; then
  echo "=================================================================="
  echo " ERROR: Please run this single-file installer with sudo:"
  echo "   sudo bash $0"
  echo "=================================================================="
  exit 1
fi

REAL_USER="${SUDO_USER:-$(logname 2>/dev/null || echo root)}"
REAL_HOME="$(getent passwd "${REAL_USER}" | cut -d: -f6 2>/dev/null || echo /root)"

echo "=================================================================="
echo " Karobar-App ERP v2 — Single-File Linux Server Turnkey Installer"
echo "------------------------------------------------------------------"
echo " Target Directory : ${APP_DIR}"
echo " LAN Port         : ${SERVER_PORT}"
echo " Database Engine  : MariaDB 10.11 (InnoDB) + Redis 7"
echo " Desktop User     : ${REAL_USER}"
echo "=================================================================="

export DEBIAN_FRONTEND=noninteractive

# ------------------------------------------------------------------------------
# STEP 1: Install System Packages
# ------------------------------------------------------------------------------
echo "[1/8] Installing system packages (Nginx, MariaDB, Redis, PHP 8.4, Node 20, Avahi mDNS)..."
apt-get update -y
apt-get install -y software-properties-common curl ca-certificates gnupg2 lsb-release \
  ufw git unzip zip rsync sqlite3 mariadb-server mariadb-client redis-server nginx \
  avahi-daemon avahi-utils xdg-utils

if ! apt-cache show php8.4-fpm >/dev/null 2>&1; then
  add-apt-repository -y ppa:ondrej/php || true
  apt-get update -y
fi

apt-get install -y \
  php8.4-fpm php8.4-cli php8.4-common php8.4-mysql php8.4-sqlite3 \
  php8.4-mbstring php8.4-xml php8.4-zip php8.4-gd php8.4-curl \
  php8.4-bcmath php8.4-intl php8.4-redis php8.4-opcache

if ! command -v composer >/dev/null 2>&1; then
  curl -sS https://getcomposer.org/installer | php -- --install-dir=/usr/local/bin --filename=composer
fi

if ! command -v node >/dev/null 2>&1; then
  curl -fsSL https://deb.nodesource.com/setup_20.x | bash -
  apt-get install -y nodejs
fi

# ------------------------------------------------------------------------------
# STEP 2: Embed & Configure MariaDB (InnoDB) + Redis + Avahi mDNS
# ------------------------------------------------------------------------------
echo "[2/8] Writing embedded MariaDB InnoDB & Avahi mDNS configurations..."
mkdir -p /etc/mysql/mariadb.conf.d
cat >/etc/mysql/mariadb.conf.d/99-karobar-innodb.cnf <<'EOF'
[mysqld]
character-set-server = utf8mb4
collation-server = utf8mb4_unicode_ci
default-storage-engine = InnoDB
innodb_buffer_pool_size = 1G
innodb_log_file_size = 256M
innodb_flush_log_at_trx_commit = 1
innodb_ flush_method = O_DIRECT
innodb_lock_wait_timeout = 15
transaction-isolation = READ-COMMITTED
max_connections = 200
max_allowed_packet = 128M
EOF
sed -i 's/innodb_ flush_method/innodb_flush_method/g' /etc/mysql/mariadb.conf.d/99-karobar-innodb.cnf

mkdir -p /etc/avahi/services
cat >/etc/avahi/services/karobar.service <<EOF
<?xml version="1.0" standalone='no'?>
<!DOCTYPE service-group SYSTEM "avahi-service.dtd">
<service-group>
  <name replace-wildcards="yes">Karobar Store Server on %h</name>
  <service>
    <type>_http._tcp</type>
    <port>${SERVER_PORT}</port>
  </service>
</service-group>
EOF

systemctl enable --now mariadb
systemctl enable --now redis-server
systemctl enable --now avahi-daemon
systemctl restart mariadb

mysql -u root <<EOF
CREATE DATABASE IF NOT EXISTS \`${DB_NAME}\` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS '${DB_USER}'@'localhost' IDENTIFIED BY '${DB_PASS}';
CREATE USER IF NOT EXISTS '${DB_USER}'@'127.0.0.1' IDENTIFIED BY '${DB_PASS}';
ALTER USER '${DB_USER}'@'localhost' IDENTIFIED BY '${DB_PASS}';
ALTER USER '${DB_USER}'@'127.0.0.1' IDENTIFIED BY '${DB_PASS}';
GRANT ALL PRIVILEGES ON \`${DB_NAME}\`.* TO '${DB_USER}'@'localhost';
GRANT ALL PRIVILEGES ON \`${DB_NAME}\`.* TO '${DB_USER}'@'127.0.0.1';
FLUSH PRIVILEGES;
EOF

# ------------------------------------------------------------------------------
# STEP 3: Deploy Application Code into /opt/karobar-server
# ------------------------------------------------------------------------------
echo "[3/8] Deploying Karobar-App ERP source to ${APP_DIR}..."
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if [[ -f "${SCRIPT_DIR}/artisan" ]]; then
  SOURCE_ROOT="${SCRIPT_DIR}"
elif [[ -f "${SCRIPT_DIR}/../../artisan" ]]; then
  SOURCE_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
else
  SOURCE_ROOT=""
fi

mkdir -p "${APP_DIR}"
if [[ -n "${SOURCE_ROOT}" && "${SOURCE_ROOT}" != "${APP_DIR}" ]]; then
  rsync -a --exclude='.git' --exclude='node_modules' --exclude='dist' "${SOURCE_ROOT}/" "${APP_DIR}/"
elif [[ -z "${SOURCE_ROOT}" && -n "${GIT_REPO_URL}" ]]; then
  git clone "${GIT_REPO_URL}" "${APP_DIR}"
fi

cd "${APP_DIR}"
mkdir -p storage/app/public storage/app/backups storage/app/imports/sqlite storage/app/whatsapp-auth \
  storage/framework/cache storage/framework/sessions storage/framework/views \
  storage/logs bootstrap/cache

# ------------------------------------------------------------------------------
# STEP 4: Install Dependencies & Build Production Assets (if not pre-bundled)
# ------------------------------------------------------------------------------
echo "[4/8] Preparing Composer & Frontend production bundle..."
rm -f "${APP_DIR}/public/hot"
if [[ ! -f "${APP_DIR}/vendor/autoload.php" ]]; then
  COMPOSER_ALLOW_SUPERUSER=1 composer install --no-dev --optimize-autoloader --no-interaction
else
  COMPOSER_ALLOW_SUPERUSER=1 composer dump-autoload --optimize --no-interaction || true
fi

if [[ ! -f "${APP_DIR}/public/build/manifest.json" ]]; then
  npm ci --no-audit --no-fund
  npm run build
fi

# ------------------------------------------------------------------------------
# STEP 5: Configure .env, Run Migrations, Seed Admin & Launch Token
# ------------------------------------------------------------------------------
echo "[5/8] Configuring .env, running migrations, and initializing Admin account..."
if [[ ! -f "${APP_DIR}/.env" ]]; then
  cp "${APP_DIR}/.env.example" "${APP_DIR}/.env"
  php artisan key:generate --force
fi

LAN_IP="$(hostname -I 2>/dev/null | awk '{print $1}')"
LAN_IP="${LAN_IP:-127.0.0.1}"

sed -i "s|^APP_ENV=.*|APP_ENV=production|g" "${APP_DIR}/.env"
sed -i "s|^APP_DEBUG=.*|APP_DEBUG=false|g" "${APP_DIR}/.env"
sed -i "s|^APP_URL=.*|APP_URL=https://${LAN_IP}:${SERVER_PORT}|g" "${APP_DIR}/.env"
sed -i "s|^DB_CONNECTION=.*|DB_CONNECTION=mariadb|g" "${APP_DIR}/.env"
sed -i "s|^[[:space:]#]*DB_HOST=.*|DB_HOST=127.0.0.1|g" "${APP_DIR}/.env"
sed -i "s|^[[:space:]#]*DB_PORT=.*|DB_PORT=3306|g" "${APP_DIR}/.env"
sed -i "s|^[[:space:]#]*DB_DATABASE=.*|DB_DATABASE=${DB_NAME}|g" "${APP_DIR}/.env"
sed -i "s|^[[:space:]#]*DB_USERNAME=.*|DB_USERNAME=${DB_USER}|g" "${APP_DIR}/.env"
sed -i "s|^[[:space:]#]*DB_PASSWORD=.*|DB_PASSWORD=${DB_PASS}|g" "${APP_DIR}/.env"
sed -i "s|^CACHE_STORE=.*|CACHE_STORE=redis|g" "${APP_DIR}/.env"
sed -i "s|^SESSION_DRIVER=.*|SESSION_DRIVER=redis|g" "${APP_DIR}/.env"

php artisan migrate --force
php artisan db:seed --class=RolesAndPermissionsSeeder --force || true

# Ensure an Administrator user exists and has Admin role
php artisan tinker --execute="
\$u = \App\Models\User::first();
if (!\$u) {
    \$u = \App\Models\User::create([
        'name' => '${ADMIN_NAME}',
        'email' => '${ADMIN_EMAIL}',
        'password' => bcrypt('${ADMIN_PASS}'),
        'status' => 'active',
    ]);
}
foreach (['sanctum', 'web'] as \$g) {
    try {
        \$r = \Spatie\Permission\Models\Role::firstOrCreate(['name' => 'Admin', 'guard_name' => \$g]);
        \$u->assignRole(\$r);
    } catch (\Throwable \$e) {}
}
\$s = \App\Models\Setting::firstOrCreate(['id' => 1]);
if (empty(\$s->network_server_token)) {
    \$s->network_server_token = 'KRB-NET-' . strtoupper(\Illuminate\Support\Str::random(12));
    \$s->terminal_auto_authorize = true;
    \$s->save();
}
" >/dev/null 2>&1 || true

php artisan storage:link >/dev/null 2>&1 || true

# Generate local quick-launch key for instant Server Dashboard opening
LAUNCH_KEY="$(openssl rand -hex 24)"
echo "${LAUNCH_KEY}" > "${APP_DIR}/storage/app/server-launch.key"
chmod 640 "${APP_DIR}/storage/app/server-launch.key"

# Optional: Import existing SQLite database if provided
if [[ -n "${IMPORT_SQLITE}" && -f "${IMPORT_SQLITE}" ]]; then
  echo "[5b/8] Importing existing SQLite database (${IMPORT_SQLITE}) into MariaDB..."
  php artisan karobar:import-sqlite "${IMPORT_SQLITE}" --mode=replace
fi

# ------------------------------------------------------------------------------
# STEP 6: Embed & Configure PHP-FPM Pool, Nginx, Systemd & karobar-ctl
# ------------------------------------------------------------------------------
echo "[6/8] Writing embedded PHP-FPM, Nginx, Systemd, and karobar-ctl configurations..."

cat >/etc/php/8.4/fpm/pool.d/karobar.conf <<'EOF'
[karobar]
user = www-data
group = www-data
listen = /run/php/php8.4-fpm-karobar.sock
listen.owner = www-data
listen.group = www-data
listen.mode = 0660

pm = dynamic
pm.max_children = 32
pm.start_servers = 6
pm.min_spare_servers = 4
pm.max_spare_servers = 12
pm.max_requests = 1000

php_admin_value[memory_limit] = 512M
php_admin_value[upload_max_filesize] = 256M
php_admin_value[post_max_size] = 256M
php_admin_value[max_execution_time] = 600
EOF

mkdir -p /etc/ssl/karobar
if [[ ! -f /etc/ssl/karobar/karobar.crt || ! -f /etc/ssl/karobar/karobar.key ]]; then
  openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
    -keyout /etc/ssl/karobar/karobar.key \
    -out /etc/ssl/karobar/karobar.crt \
    -subj "/CN=${LAN_IP}" \
    -addext "subjectAltName=IP:${LAN_IP},IP:127.0.0.1,DNS:localhost,DNS:$(hostname).local" >/dev/null 2>&1 || \
  openssl req -x509 -nodes -days 3650 -newkey rsa:2048 \
    -keyout /etc/ssl/karobar/karobar.key \
    -out /etc/ssl/karobar/karobar.crt \
    -subj "/CN=${LAN_IP}" >/dev/null 2>&1
  chmod 600 /etc/ssl/karobar/karobar.key
fi

cat >/etc/nginx/sites-available/karobar.conf <<EOF
server {
    listen ${SERVER_PORT} ssl default_server;
    listen [::]:${SERVER_PORT} ssl default_server;
    server_name _;

    ssl_certificate /etc/ssl/karobar/karobar.crt;
    ssl_certificate_key /etc/ssl/karobar/karobar.key;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;

    root ${APP_DIR}/public;
    index index.php index.html;

    charset utf-8;
    client_max_body_size 256M;
    keepalive_timeout 65;

    gzip on;
    gzip_types text/plain text/css application/json application/javascript text/xml application/xml application/xml+rss text/javascript;

    location / {
        try_files \$uri \$uri/ /index.php?\$query_string;
    }

    location = /favicon.ico { access_log off; log_not_found off; }
    location = /robots.txt  { access_log off; log_not_found off; }

    error_page 404 /index.php;

    location ~ \.php\$ {
        fastcgi_pass unix:/run/php/php8.4-fpm-karobar.sock;
        fastcgi_param SCRIPT_FILENAME \$realpath_root\$fastcgi_script_name;
        include fastcgi_params;
        fastcgi_read_timeout 600;
        fastcgi_buffers 16 16k;
        fastcgi_buffer_size 32k;
    }

    location ~ /\.(?!well-known).* {
        deny all;
    }
}
EOF

ln -sf /etc/nginx/sites-available/karobar.conf /etc/nginx/sites-enabled/karobar.conf
rm -f /etc/nginx/sites-enabled/default

cat >/etc/systemd/system/karobar-scheduler.service <<EOF
[Unit]
Description=Karobar-App ERP Laravel Scheduler
After=network.target mariadb.service redis-server.service

[Service]
Type=simple
User=www-data
Group=www-data
WorkingDirectory=${APP_DIR}
ExecStart=/usr/bin/php ${APP_DIR}/artisan schedule:work
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

cat >/etc/systemd/system/karobar-whatsapp.service <<EOF
[Unit]
Description=Karobar-App ERP Central WhatsApp Agent Sidecar
After=network.target

[Service]
Type=simple
User=www-data
Group=www-data
WorkingDirectory=${APP_DIR}
Environment=WHATSAPP_AGENT_PORT=3847
Environment=WHATSAPP_AUTH_DIR=${APP_DIR}/storage/app/whatsapp-auth
ExecStart=/usr/bin/node ${APP_DIR}/desktop/whatsapp-agent/server.cjs
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF

cat >/usr/local/bin/karobar-ctl <<EOF
#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${APP_DIR}"
SERVER_PORT="${SERVER_PORT}"
CMD="\${1:-help}"
shift || true

case "\${CMD}" in
  dashboard|open)
    KEY="\$(cat "\${APP_DIR}/storage/app/server-launch.key" 2>/dev/null || echo '')"
    URL="https://127.0.0.1:\${SERVER_PORT}/server-launch"
    echo "Opening Karobar Server & Terminals Dashboard: \${URL}"
    if command -v xdg-open >/dev/null 2>&1; then
      xdg-open "\${URL}" >/dev/null 2>&1 &
    fi
    ;;

  status|doctor)
    echo "=== Karobar-App Linux Store Server Status ==="
    LAN_IP="\$(hostname -I 2>/dev/null | awk '{print \$1}')"
    echo "Server LAN URL    : http://\${LAN_IP:-127.0.0.1}:\${SERVER_PORT}"
    echo "Server Dashboard  : http://\${LAN_IP:-127.0.0.1}:\${SERVER_PORT}/server-dashboard"
    echo "mDNS URL          : http://\$(hostname).local:\${SERVER_PORT}"
    echo ""
    systemctl --no-pager status mariadb redis-server php8.4-fpm nginx karobar-scheduler --lines=0 || true
    ;;

  terminals)
    cd "\${APP_DIR}"
    php artisan tinker --execute="
      \\\$terms = App\\Models\\Terminal::orderByDesc('last_seen_at')->get(['id','name','ip_address','mode','is_authorized','last_seen_at']);
      foreach (\\\$terms as \\\$t) {
        echo sprintf('#%d | %-20s | %-15s | %-10s | Auth:%s | Seen:%s', \\\$t->id, \\\$t->name, \\\$t->ip_address ?? '-', \\\$t->mode, \\\$t->is_authorized ? 'YES' : 'NO', \\\$t->last_seen_at ?? 'Never') . PHP_EOL;
      }
    "
    ;;

  import-sqlite)
    FILE="\${1:-}"
    if [[ -z "\${FILE}" ]]; then
      echo "Usage: sudo karobar-ctl import-sqlite /path/to/database.sqlite [--mode=replace|merge]"
      exit 1
    fi
    shift || true
    cd "\${APP_DIR}"
    sudo -u www-data php artisan karobar:import-sqlite "\${FILE}" "\$@"
    ;;

  export-sqlite)
    OUT="\${1:-/tmp/karobar-export-\$(date +%Y%m%d-%H%M%S).sqlite}"
    cd "\${APP_DIR}"
    sudo -u www-data php artisan karobar:export-sqlite "\${OUT}"
    ;;

  backup)
    cd "\${APP_DIR}"
    sudo -u www-data php artisan tinker --execute="print_r(app(App\\Services\\BackupService::class)->createBackup('database', 'CLI Manual Server Backup'));"
    ;;

  restart)
    systemctl restart mariadb redis-server php8.4-fpm nginx karobar-scheduler karobar-whatsapp
    echo "All Karobar Server services restarted."
    ;;

  logs)
    tail -n 100 -f "\${APP_DIR}/storage/logs/laravel.log"
    ;;

  machine-id)
    cd "\${APP_DIR}"
    php artisan tinker --execute="echo app(App\\Services\\LicenseService::class)->computeMachineId() . PHP_EOL;"
    ;;

  *)
    echo "Karobar-App Linux Server Control Utility (karobar-ctl)"
    echo "Commands:"
    echo "  sudo karobar-ctl dashboard                         Open Server & Terminals Dashboard in browser"
    echo "  sudo karobar-ctl status                            Show LAN IP & service health"
    echo "  sudo karobar-ctl terminals                         List all registered POS terminals"
    echo "  sudo karobar-ctl import-sqlite <file.sqlite>       Import existing SQLite database into MariaDB"
    echo "  sudo karobar-ctl export-sqlite [output.sqlite]     Export MariaDB into portable .sqlite file"
    echo "  sudo karobar-ctl backup                            Create immediate database backup"
    echo "  sudo karobar-ctl restart                           Restart all Karobar server services"
    echo "  sudo karobar-ctl logs                              Tail live server logs"
    echo "  sudo karobar-ctl machine-id                        Display hardware Machine ID for licensing"
    ;;
esac
EOF
chmod +x /usr/local/bin/karobar-ctl

chown -R www-data:www-data "${APP_DIR}/storage" "${APP_DIR}/bootstrap/cache"

systemctl daemon-reload
systemctl restart php8.4-fpm
systemctl restart nginx
systemctl enable --now karobar-scheduler.service
systemctl enable --now karobar-whatsapp.service || true

# ------------------------------------------------------------------------------
# STEP 7: Configure Firewall & Create Desktop Launcher Icon
# ------------------------------------------------------------------------------
echo "[7/8] Configuring Firewall (UFW) and creating Desktop Shortcut..."
ufw allow OpenSSH >/dev/null 2>&1 || true
ufw allow "${SERVER_PORT}/tcp" >/dev/null 2>&1 || true
ufw allow 5353/udp >/dev/null 2>&1 || true

DASHBOARD_LAUNCH_URL="https://127.0.0.1:${SERVER_PORT}/server-launch"

DESKTOP_ENTRY="[Desktop Entry]
Version=1.0
Type=Application
Name=Karobar Server & Terminals Dashboard
Comment=Connect POS Terminals, Import/Export Database, and Troubleshoot LAN Server
Exec=xdg-open ${DASHBOARD_LAUNCH_URL}
Icon=network-server
Terminal=false
Categories=Office;Finance;Network;
"

mkdir -p /usr/share/applications
echo "${DESKTOP_ENTRY}" >/usr/share/applications/karobar-server-dashboard.desktop
chmod +x /usr/share/applications/karobar-server-dashboard.desktop

if [[ -d "${REAL_HOME}/Desktop" ]]; then
  echo "${DESKTOP_ENTRY}" >"${REAL_HOME}/Desktop/Karobar-Server-Dashboard.desktop"
  chown "${REAL_USER}:${REAL_USER}" "${REAL_HOME}/Desktop/Karobar-Server-Dashboard.desktop" || true
  chmod +x "${REAL_HOME}/Desktop/Karobar-Server-Dashboard.desktop" || true
fi

# ------------------------------------------------------------------------------
# STEP 8: Auto-Open Server Dashboard in Browser & Print Summary
# ------------------------------------------------------------------------------
echo "[8/8] Reading Hardware Machine ID and launching Server Dashboard..."
MACHINE_ID="$(php artisan tinker --execute="echo app(App\Services\LicenseService::class)->computeMachineId();" 2>/dev/null || echo "Run: sudo karobar-ctl machine-id")"

echo ""
echo "=================================================================="
echo " KAROBAR-APP LINUX STORE SERVER INSTALLED SUCCESSFULLY!"
echo "=================================================================="
echo " Server LAN URL (IP)   : http://${LAN_IP}:${SERVER_PORT}"
echo " Server mDNS URL       : http://$(hostname).local:${SERVER_PORT}"
echo " Server Dashboard URL  : http://${LAN_IP}:${SERVER_PORT}/server-dashboard"
echo " Default Admin Login   : ${ADMIN_EMAIL}  /  ${ADMIN_PASS}"
echo " Hardware Machine ID   : ${MACHINE_ID}"
echo " Open Dashboard Again  : sudo karobar-ctl dashboard"
echo "=================================================================="

# Automatically open the browser to the Server Dashboard if a GUI display is active
if [[ -n "${DISPLAY:-}" || -n "${WAYLAND_DISPLAY:-}" ]]; then
  echo "Launching Server & Terminals Dashboard in your browser..."
  if [[ "${REAL_USER}" != "root" ]]; then
    sudo -u "${REAL_USER}" xdg-open "${DASHBOARD_LAUNCH_URL}" >/dev/null 2>&1 &
  else
    xdg-open "${DASHBOARD_LAUNCH_URL}" >/dev/null 2>&1 &
  fi
fi
