# Karobar-App ERP v2 — Planning Context

## Project Overview
Karobar-App ERP is an enterprise-grade desktop & web accounting, inventory, and POS platform built with Laravel 11 (PHP 8.2+), React 18, Vite, and Electron.

## Architecture
- **Desktop Host**: Electron with native Node gateway load-balancer and multi-worker PHP pool.
- **Backend**: Laravel 11 running on SQLite in WAL mode with robust busy timeouts and automatic checkpoints.
- **Frontend**: React 18, Tailwind CSS, Heroicons, Headless UI, and custom theme systems.
- **IPC Layer**: Context-isolated Electron preload bridge (`window.karobarDesktop`).

## Active Milestone: Native Multi-Window Architecture (v2026.11)
Replacing in-page iframe tab management with true native desktop multi-windowing (`BrowserWindow` per page/document) for multi-monitor, snap-assist, and zero-iframe-overhead workflows.
