# Karobar-App ERP v2 — Planning State

## Current Focus
**Milestone:** Native Multi-Window Architecture
**Phase:** 01-multi-window-architecture
**Status:** Completed & Verified

## Decisions Log
- **D-01 (Multi-Window vs Tabs):** Complete removal of iframe-based `SubWindowManager` tabs in favor of native Electron `BrowserWindow` instances.
- **D-02 (Shared Session):** All windows share the same Electron partition/session, ensuring authenticated state, cookies, and local database access without re-login.
- **D-03 (Graceful Lifecycle):** Window manager tracks open windows; closing an individual window does not kill the app; closing the last window triggers clean app quit.
- **D-04 (Fallback & Web Compatibility):** Outside Electron, `openWindow` gracefully falls back to `window.open(path, '_blank')`.

## Completed Plans
- `01-01`: Native Multi-Window Architecture & Frontend Modernization
