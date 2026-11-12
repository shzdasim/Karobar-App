---
phase: 01-multi-window-architecture
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - desktop/main.cjs
  - desktop/preload.cjs
  - resources/js/layouts/DashboardLayout.jsx
  - resources/js/components/Topbar.jsx
  - resources/js/components/sidebar-templates/ClassicSidebar.jsx
  - resources/js/components/PageSearchModal.jsx
autonomous: true
requirements:
  - REQ-MW-01
  - REQ-MW-02
  - REQ-MW-03
  - REQ-MW-04
  - REQ-MW-05
  - REQ-MW-06

estimate:
  tokens: 45000
  raw_tokens: 22000
  tasks: 4
  confidence: high

must_haves:
  truths:
    - User can open multiple independent native desktop windows simultaneously.
    - Each window has its own native controls and full application menu with "Window -> New Window / Close Window".
    - Closing any window does not crash or close the app until the last window is closed.
    - The in-page iframe tab manager is removed, rendering content directly with zero iframe latency.
  artifacts:
    - desktop/main.cjs
    - desktop/preload.cjs
    - resources/js/layouts/DashboardLayout.jsx
  key_links:
    - window.karobarDesktop.openWindow to desktop/main.cjs createAppWindow
---

<objective>
Implement native Electron multi-window architecture in Karobar-App ERP v2, replacing the iframe-based tab system with independent native OS windows sharing authenticated state, local database, and complete menu capabilities.
</objective>

<tasks>

<task type="tracer">
  <name>Task 1: Native Electron Multi-Window Engine & Window Menu</name>
  <files>desktop/main.cjs, desktop/preload.cjs</files>
  <action>
    Refactor desktop/main.cjs to support a centralized multi-window registry:
    - Maintain `activeAppWindows = new Set()`.
    - Create `createAppWindow(routePath, options)` that creates a new BrowserWindow with standard preload, shared session, menu, and popup handlers.
    - In `createMainWindow()`, delegate to `createAppWindow("/")`.
    - Manage window lifecycle: on window "closed", delete from `activeAppWindows`. If `activeAppWindows.size === 0`, call `app.quit()`.
    - Implement IPC handler `karobar:open-window` to spawn new native windows.
    - Update `setWindowOpenHandler` on all created webContents to spawn new native app windows for internal routes instead of denying or popup previews.
    - Replace the "Workspace Tabs" menu with a native "Window" menu featuring "New Window" (Ctrl+N), "Close Window" (Ctrl+W), "Minimize" (Ctrl+M), "Zoom", and a list of active open windows with click-to-focus.
    - In desktop/preload.cjs, expose `openWindow(path, title)` via `window.karobarDesktop.openWindow`.
  </action>
  <verify>
    <automated>node -c desktop/main.cjs && node -c desktop/preload.cjs</automated>
  </verify>
  <done>
    Electron main process compiles cleanly with native multi-window lifecycle, IPC handlers, and native Window menu.
  </done>
</task>

<task type="auto">
  <name>Task 2: DashboardLayout Direct Rendering & Multi-Window Bridge</name>
  <files>resources/js/layouts/DashboardLayout.jsx</files>
  <action>
    Deconstruct the iframe-based subwindow system in DashboardLayout.jsx:
    - Remove the `SubWindowManager` component rendering and iframe state matrices.
    - Render `{children}` directly in the layout with full responsive width and height.
    - Replace `karobar:open-subwindow` listener with a unified window opener: when desktop API `window.karobarDesktop?.openWindow` is present, invoke it; otherwise use `window.open(path, '_blank')`.
    - Provide a clean global helper or CustomEvent `karobar:open-window` so any component can request opening a page in a new native window.
  </action>
  <verify>
    <automated>npm run build</automated>
  </verify>
  <done>
    DashboardLayout renders child components directly without iframes, and dispatches native window opening requests.
  </done>
</task>

<task type="auto">
  <name>Task 3: Modernize Topbar, Sidebar, and Page Search to "Open in New Window"</name>
  <files>resources/js/components/Topbar.jsx, resources/js/components/sidebar-templates/ClassicSidebar.jsx, resources/js/components/PageSearchModal.jsx</files>
  <action>
    Update navigation components:
    - In ClassicSidebar.jsx: Update "Open in New Tab" icons/tooltips/handlers to "Open in New Window", invoking `openInNewWindow(path, title)`.
    - In Topbar.jsx: Update new tab shortcuts or window actions to open in new native window.
    - In PageSearchModal.jsx: Update tab search mode to "Open in New Window".
  </action>
  <verify>
    <automated>npm run build</automated>
  </verify>
  <done>
    All navigation UI controls consistently prompt and trigger native window creation.
  </done>
</task>

<task type="auto">
  <name>Task 4: Build Verification & End-to-End Test Suite</name>
  <files>tests/Feature/IroncladLicenseSystemTest.php, tests/Feature/WindowsInstallerAndSetupGenieTest.php</files>
  <action>
    Execute full compilation and test validation:
    - Run `npm run build` to verify frontend production bundle integrity.
    - Run `php artisan test` on feature suites to verify API stability.
  </action>
  <verify>
    <automated>npm run build && php artisan test --filter=WindowsInstallerAndSetupGenieTest</automated>
  </verify>
  <done>
    Production assets built cleanly and backend automated test suite passes with zero errors.
  </done>
</task>

</tasks>

<threat_model>
## Trust Boundaries
| Boundary | Description |
|----------|-------------|
| Renderer → Electron Main | Untrusted/renderer paths crossing IPC `karobar:open-window` |

## STRIDE Threat Register
| Threat ID | Category | Component | Severity | Disposition | Mitigation Plan |
|-----------|----------|-----------|----------|-------------|-----------------|
| T-01-01 | Elevation of Privilege | desktop/main.cjs | medium | mitigate | Validate that opened URLs strictly resolve to localhost activePort and authorized HTTP/HTTPS/local paths, denying file:/// or protocol injection |
| T-01-SC | Tampering | npm dependencies | low | accept | No new npm packages introduced; using existing native Electron BrowserWindow APIs |
</threat_model>

<verification>
Automated compilation checks for Electron scripts and Vite frontend build.
</verification>

<success_criteria>
Multi-window architecture fully operational in Electron with zero iframe overhead in DashboardLayout.
</success_criteria>
