#!/usr/bin/env bash
# ==============================================================================
# Karobar-App ERP v2 — Linux Store Server CLI Control Utility (karobar-ctl)
# ==============================================================================

set -euo pipefail

APP_DIR="${KAROBAR_APP_DIR:-/opt/karobar-server}"
CMD="${1:-status}"
shift || true

cd "${APP_DIR}"

case "${CMD}" in
  status)
    echo "=== Karobar-App Linux Server Status ==="
    echo "LAN IP(s) : $(hostname -I)"
    systemctl is-active --quiet nginx && echo "Nginx     : ACTIVE" || echo "Nginx     : STOPPED"
    systemctl is-active --quiet php8.3-fpm && echo "PHP-FPM   : ACTIVE" || echo "PHP-FPM   : STOPPED"
    systemctl is-active --quiet mariadb && echo "MariaDB   : ACTIVE" || echo "MariaDB   : STOPPED"
    systemctl is-active --quiet redis-server && echo "Redis     : ACTIVE" || echo "Redis     : STOPPED"
    systemctl is-active --quiet karobar-scheduler && echo "Scheduler : ACTIVE" || echo "Scheduler : STOPPED"
    systemctl is-active --quiet karobar-whatsapp && echo "WhatsApp  : ACTIVE" || echo "WhatsApp  : STOPPED"
    ;;
  machine-id)
    php artisan tinker --execute="echo app(App\Services\LicenseService::class)->computeMachineId() . PHP_EOL;"
    ;;
  backup)
    sudo -u www-data php artisan backup:run-scheduled --force
    ;;
  import-sqlite)
    if [[ $# -lt 1 ]]; then
      echo "Usage: sudo karobar-ctl import-sqlite /path/to/database.sqlite [--mode=replace|merge]"
      exit 1
    fi
    sudo -u www-data php artisan karobar:import-sqlite "$@"
    ;;
  export-sqlite)
    sudo -u www-data php artisan karobar:export-sqlite "$@"
    ;;
  restart)
    systemctl restart php8.3-fpm nginx mariadb redis-server karobar-scheduler karobar-whatsapp
    echo "All Karobar-App server services restarted."
    ;;
  logs)
    tail -n 100 -f "${APP_DIR}/storage/logs/laravel.log"
    ;;
  update)
    bash "${APP_DIR}/deploy/linux-server/update-server.sh"
    ;;
  *)
    echo "Usage: karobar-ctl {status|machine-id|backup|import-sqlite|export-sqlite|restart|logs|update}"
    exit 1
    ;;
esac
