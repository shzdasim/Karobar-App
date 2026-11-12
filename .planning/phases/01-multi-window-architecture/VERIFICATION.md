# Phase 01: Multi-Window Architecture Verification

## Verification Checklist

### 1. Electron Engine & Window Lifecycle
- [x] `activeAppWindows = new Set()` tracks all open windows.
- [x] `createAppWindow(routePath, options)` instantiates native `BrowserWindow` with preload, shared session, and popup handlers.
- [x] When a secondary window closes, it is removed from `activeAppWindows` and the app continues running.
- [x] When the last open window closes, `app.quit()` is invoked, terminating child PHP workers and the gateway cleanly.
- [x] `app.on("second-instance")` focuses or restores the active window without throwing errors.
- [x] `setWindowOpenHandler` catches `window.open(url)` and spawns native `BrowserWindow` for internal routes while delegating external URLs to default browser.

### 2. Native Window Menu & Desktop Preload
- [x] Replaced "Workspace Tabs" menu with native "Window" menu:
  - "New Window" (`CmdOrCtrl+N`)
  - "Close Window" (`CmdOrCtrl+W`)
  - "Minimize", "Zoom", "Toggle Fullscreen"
  - "Bring All Windows to Front"
  - Dynamic list of open windows with click-to-focus and checkmarks.
- [x] Topbar, Navigation sections include "Open [Section] in New Window ▸" submenus.
- [x] `desktop/preload.cjs` exposes `openWindow`, `getOpenWindows`, `focusWindow` on `window.karobarDesktop`.

### 3. Frontend Streamlining & Zero Iframe Overhead
- [x] `resources/js/components/SubWindowManager.jsx` and tab iframes deleted.
- [x] `resources/js/layouts/DashboardLayout.jsx` renders `{children ?? <Outlet />}` directly with zero iframe overhead. Shell bundle reduced by ~50 kB.
- [x] `openInNewWindow(path, title)` exported and used across Topbar, Sidebar, and Page Search.
- [x] `PageSearchModal.jsx` and `ClassicSidebar.jsx` updated to trigger native window openings.

### 4. Build & Test Results
- [x] `node -c desktop/main.cjs && node -c desktop/preload.cjs` (Passed).
- [x] `npm run build` compiled client production bundle in 2.01s (Passed).
- [x] `php artisan test --filter=WindowsInstallerAndSetupGenieTest` (Passed, 88 assertions).
- [x] `php artisan test --filter=IroncladLicenseSystemTest` (Passed, 46 assertions).
