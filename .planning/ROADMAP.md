# Karobar-App ERP v2 — Roadmap

## Milestone: Native Multi-Window Architecture

### Phase 1: Multi-Window Native Engine & Frontend Modernization
**Goal:** Transition from in-page iframe-based tabs to native Electron multi-window architecture so users can open multiple pages simultaneously on single or multiple displays with native OS window management.
**Status:** In Progress
**Requirements:**
- REQ-MW-01: Native Electron `BrowserWindow` spawner supporting independent windows for any route (/sales/create, /customer-ledger, /purchase-orders, etc.).
- REQ-MW-02: Native window lifecycle tracking: application remains alive while any window is open; clean shutdown when last window closes.
- REQ-MW-03: Electron application menu "Window" section with New Window (`Ctrl+N`), Close Window (`Ctrl+W`), Zoom, Minimize, and active window list.
- REQ-MW-04: Electron `setWindowOpenHandler` and IPC bridge (`karobar:open-window`) to seamlessly convert `window.open()` and explicit multi-window actions into full native windows.
- REQ-MW-05: Remove iframe-based `SubWindowManager` from `DashboardLayout.jsx`, rendering views directly in the primary layout.
- REQ-MW-06: Update Topbar, Sidebar, and Page Search modals to trigger native window openings.
