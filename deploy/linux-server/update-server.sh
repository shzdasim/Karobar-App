#!/usr/bin/env bash
# ==============================================================================
# Karobar-App ERP v2 — Zero-Downtime Linux Store Server Updater
# ==============================================================================

set -euo pipefail

APP_DIR="${KAROBAR_APP_DIR:-/opt/karobar-server}"

if [[ $EUID -ne 0 ]]; then
  echo "ERROR: Please run as root (sudo bash update-server.sh)"
  exit 1
fi

cd "${APP_DIR}"

echo "[1/5] Creating pre-update safety database backup..."
sudo -u www-data php artisan backup:run-scheduled --force || true

echo "[2/5] Pulling latest application code..."
if [[ -d ".git" ]]; then
  git pull --ff-only
fi

echo "[3/5] Updating Composer & NPM packages and building frontend..."
COMPOSER_ALLOW_SUPERUSER=1 composer install --no-dev --optimize-autoloader --no-interaction
npm ci --no-audit --no-fund
npm run build

echo "[4/5] Running database migrations and clearing caches..."
php artisan migrate --force
php artisan db:seed --class=PermissionSeeder --force || true
php artisan optimize:clear
chown -R www-data:www-data "${APP_DIR}/storage" "${APP_DIR}/bootstrap/cache"

echo "[5/5] Reloading PHP-FPM, Nginx, Scheduler, and WhatsApp services..."
systemctl reload php8.3-fpm
systemctl reload nginx
systemctl restart karobar-scheduler.service || true
systemctl restart karobar-whatsapp.service || true

echo "Karobar-App Linux Server updated successfully!"
