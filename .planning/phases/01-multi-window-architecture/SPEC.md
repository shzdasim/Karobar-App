# Phase 01: Multi-Window Architecture Specification

## Context & Motivation
The previous in-page iframe-based tab system (`SubWindowManager.jsx`) resulted in nested scrollbars, cross-iframe event synchronization overhead, state reload anomalies, and restricted the user to a single OS window. 
Users need to view and edit multiple documents concurrently across separate windows and multi-monitor setups (e.g., viewing Customer Ledger side-by-side with Sale Invoice creation).

## Core Requirements
1. **Native Electron Multi-Window Spawner (`desktop/main.cjs`)**:
   - `createAppWindow(initialPath, options)` capable of creating independent `BrowserWindow` instances.
   - All windows configure standard menu, preload script, shared web session, and icon.
   - Central window registry (`activeAppWindows = new Set()`) to track open windows.
   - Window lifecycle: closing a secondary window removes it from the set; closing the final open window cleanly invokes `app.quit()` to terminate backend PHP workers and the gateway.
2. **IPC & Window Open Bridge (`desktop/preload.cjs` & `desktop/main.cjs`)**:
   - IPC handler `karobar:open-window` accepting `{ path, title, width, height }`.
   - Native `setWindowOpenHandler` mapping standard `window.open(url, '_blank')` calls to native Electron app windows.
   - Expose `window.karobarDesktop.openWindow(path, title)` in preload.
3. **Application Menu Evolution**:
   - Replace "Workspace Tabs" menu with standard native "Window" menu:
     - New Window (`CmdOrCtrl+N`)
     - Close Window (`CmdOrCtrl+W`)
     - Minimize (`CmdOrCtrl+M`)
     - Zoom / Maximize
     - Open Windows listing with focus switcher
4. **Frontend Modernization (`resources/js/...`)**:
   - In `DashboardLayout.jsx`:
     - Remove `<SubWindowManager />` and the hidden/shown iframe rendering matrix.
     - Direct rendering of `{children}` with 100% viewport fluidity.
     - Provide global utility `openInNewWindow(path, title)` that uses `window.karobarDesktop.openWindow` when available, falling back to `window.open(path, '_blank')`.
   - In Topbar, Sidebar (`ClassicSidebar.jsx`), and Page Search (`PageSearchModal.jsx`):
     - Replace "Open in New Tab" with "Open in New Window".
     - Bind window opening to the unified `openInNewWindow` helper.
