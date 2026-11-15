#!/usr/bin/env bash
# ==============================================================================
# Karobar-App ERP v2 — Single-File Self-Extracting Linux Server Bundle Builder
# ==============================================================================
# Usage:
#   bash scripts/build-linux-server-installer.sh
#
# Output:
#   dist/karobar-linux-server-installer.run
#
# What it produces:
#   A single, self-contained executable (.run) file containing:
#     - Pre-built React/Vite frontend assets (public/build)
#     - PHP backend & Composer vendor packages
#     - Embedded Single-File Turnkey Installer (karobar-server-setup.sh)
#   Copy ONLY dist/karobar-linux-server-installer.run to any Ubuntu/Debian PC
#   and run:
#     sudo bash karobar-linux-server-installer.run
# ==============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${REPO_ROOT}"

DIST_DIR="${REPO_ROOT}/dist"
OUTPUT_FILE="${DIST_DIR}/karobar-linux-server-installer.run"
TMP_PAYLOAD="$(mktemp /tmp/karobar-server-payload.XXXXXX.tar.gz)"

mkdir -p "${DIST_DIR}"

echo "[1/3] Building production frontend assets (npm run build)..."
npm run build

echo "[2/3] Creating compressed application payload..."
tar -czf "${TMP_PAYLOAD}" \
  --exclude='.git' \
  --exclude='.env' \
  --exclude='node_modules' \
  --exclude='desktop/node_modules' \
  --exclude='dist' \
  --exclude='database/*.bak*' \
  --exclude='storage/app/backups/*' \
  --exclude='storage/logs/*.log' \
  --exclude='storage/framework/cache/data/*' \
  --exclude='storage/framework/sessions/*' \
  --exclude='storage/framework/views/*.php' \
  .

echo "[3/3] Assembling single-file self-extracting installer: ${OUTPUT_FILE} ..."
cat > "${OUTPUT_FILE}" <<'HEADER_EOF'
#!/usr/bin/env bash
# ==============================================================================
# KAROBAR-APP ERP v2 — SELF-EXTRACTING SINGLE-FILE LINUX SERVER INSTALLER
# ==============================================================================
# Run this single file on Ubuntu 22.04 / 24.04 or Debian 12:
#   sudo bash karobar-linux-server-installer.run
# ==============================================================================
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "=================================================================="
  echo " ERROR: Please run this installer as root:"
  echo "   sudo bash $0"
  echo "=================================================================="
  exit 1
fi

TARGET_DIR="${KAROBAR_APP_DIR:-/opt/karobar-server}"
echo "=================================================================="
echo " Extracting Karobar-App Linux Server Bundle to ${TARGET_DIR} ..."
echo "=================================================================="
mkdir -p "${TARGET_DIR}"

ARCHIVE_START_LINE="$(awk '/^__KAROBAR_ARCHIVE_BELOW__/ {print NR + 1; exit 0; }' "$0")"
tail -n +"${ARCHIVE_START_LINE}" "$0" | tar -xz -C "${TARGET_DIR}"

chmod +x "${TARGET_DIR}/karobar-server-setup.sh"
exec bash "${TARGET_DIR}/karobar-server-setup.sh" "$@"

__KAROBAR_ARCHIVE_BELOW__
HEADER_EOF

cat "${TMP_PAYLOAD}" >> "${OUTPUT_FILE}"
chmod +x "${OUTPUT_FILE}"
rm -f "${TMP_PAYLOAD}"

SIZE_MB="$(du -m "${OUTPUT_FILE}" | awk '{print $1}')"
echo ""
echo "=================================================================="
echo " SINGLE-FILE LINUX SERVER INSTALLER BUILT SUCCESSFULLY!"
echo "------------------------------------------------------------------"
echo " File  : ${OUTPUT_FILE}"
echo " Size  : ~${SIZE_MB} MB"
echo " Usage : Copy this single file to your Linux Server PC and run:"
echo "         sudo bash karobar-linux-server-installer.run"
echo "=================================================================="
