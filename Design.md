# Karobar App — Modern Glass Design System

Version 2 · Target visual direction · Companion instructions: [rule.md](rule.md)

## Purpose and direction

Karobar is a business management application for dashboards, inventory, sales, purchases, invoices, customer and supplier ledgers, reports, and settings. Its interface should feel calm, precise, and easy to use throughout a working day.

The intended direction is glassmorphism inspired by the fluid, translucent appearance of contemporary iOS interfaces: floating controls, soft rounded surfaces, subtle edge highlights, layered depth, and restrained motion. This is a web design specification, not an implementation of Apple's materials or a requirement to reproduce native iOS effects.

Use glass to establish hierarchy. Keep amounts, stock quantities, expiry warnings, and form values crisp and readable. Business tasks take priority over decorative effects.

**Status:** This is the target for the UI redesign, not a description of how every current screen looks. Existing styles are migration inputs; they should evolve toward this system. The design is not implemented by this document alone. Follow [rule.md](rule.md) when changing the application.

## The new look

The visual identity is a luminous workspace: a cool pearl canvas, floating frosted navigation, generous rounded panels, ink-colored text, and a confident cobalt action color. Teal adds a quieter supporting accent. Dark mode becomes a deep midnight workspace with smoked glass and gentle blue edge lighting.

Create the sense of glass through translucency, a fine illuminated edge, and visible separation from the canvas. Keep decoration understated so the interface still feels professional during long inventory and accounting sessions.

| Visual decision | New direction |
| --- | --- |
| Canvas | Pearl in light mode, midnight navy in dark mode; two static, low-opacity blue/teal radial washes |
| Navigation | An inset floating sidebar and topbar with frosted fill and continuous rounded outlines |
| Content | Spacious panels with a fine edge highlight, readable interiors, and soft shadows |
| Hierarchy | Large page titles, prominent amounts, quieter metadata, fewer uppercase labels |
| Color | Cobalt for primary actions, teal for supporting data, violet reserved for an additional chart series |
| Controls | Rounded rectangles for forms; pills for selection groups and small status indicators |
| Density | Comfortable by default; compact layouts only where repeated data entry benefits |
| Decoration | Static atmospheric background, small accent icon wells, restrained selected-state glow |

Avoid making every panel a different gradient, filling KPI cards with saturated colors, or adding decorative blobs within each card. Use one coherent canvas behind the whole page. Remove tiny uppercase labels, excessive outlines, and oversized drop shadows as screens migrate.

### Page composition

At desktop widths, inset the shell 16px from the viewport. Use a 248px floating sidebar with a 24px radius and a 16px gutter to the workspace. The topbar is a separate 64px glass panel with a 20px radius. The content scrolls within a predictable layout; sticky navigation must not hide headings or focused controls.

The dashboard begins with a plain title and date-range controls, followed by four consistent KPI panels. Place the main sales/purchases chart beside a smaller summary panel, then put stock alerts and near-expiry data on full-width reading surfaces. Preserve the current dashboard's metrics and calculations; these are presentation directions.

Product listings use a frosted overview panel with a large title, visible action labels, and one labeled inventory search field. Frame the inventory in glass while keeping table rows nearly opaque. Show selected rows clearly, preserve stock and deletion restrictions, and allow the table region to scroll horizontally on mobile so category, brand, and supplier remain accessible.

Product forms retain the original layout: image and identifiers first, name/formulation/pack size next, then category/brand/supplier, description, and the compact pricing row. Refresh materials with frosted framing and readable input surfaces while keeping shared typography. Keep batches beside the edit form on wide screens; contain horizontal pricing-table scrolling where needed. Do not replace this arrangement with side-by-side sections or a pricing card grid.

Category, supplier, customer, and brand directories share a glass overview with search and import/export/refresh actions. Place the add/edit form beside a wider readable list on desktop; stack them on mobile. Keep table scrolling contained and preserve permissions, validation, image upload, pagination, and deletion restrictions.

The purchase invoice list uses a glass header with labeled posted-number and supplier filters. Keep financial data on a nearly opaque table with contained scrolling and shared view/edit/delete icons. Preserve invoice type badges, amount precision, pagination, and the password-confirmed deletion workflow.

Purchase invoice entry must retain its original layout and sizing: the 12-column details row, compact item table, sticky header/footer, existing scroll region, and payment panel. Apply material and icon updates only; do not reorganize fields or resize controls. Use luminous frosted header/footer framing, a subtle accent wash, visible borders on compact and repeated table fields, borderless standard field surfaces, shaded read-only values, tinted item-column groups, and distinct totals surfaces. Avoid heavy spreadsheet gridlines and saturated title bars. Keep readable inputs, contained item-table scrolling, and shared add/remove icons. Preserve all calculations, row keyboard navigation, wholesale field visibility, payment linking, supplier search, and product creation workflows.

Purchase invoice details use the same tinted glass frame, borderless read-only detail surfaces, grouped item headings, and a distinct totals band. Keep compact item values outlined and horizontally scrollable; stack details and totals on narrow screens. Use shared action icons and preserve displayed precision, permissions, invoice search, and password-confirmed deletion.

Sale invoice lists, retail/wholesale forms, edit screens, and details share tinted glass framing and readable tables. Retain the item table beside the 280px summary on desktop, stack them on mobile, and contain item scrolling. Sale entry is an exception to borderless standard forms: its many fields use visible borders throughout. Keep the desktop summary compact so routine items and totals fit the viewport; retain overflow for long invoices, short windows, and enlarged text. Preserve sale modes, batches, stock validation, customer pricing, calculations, permissions, and existing print/receipt output.

Purchase and sale return lists use the shared glass list framing and centered icon actions. Create/edit return forms share tinted headers, bordered dense fields, grouped item tables, and a distinct totals band. Stack detail fields and totals on mobile, contain horizontal item scrolling, and preserve invoice linking, return eligibility, batches, stock limits, calculations, permissions, and save/delete flows.

Purchase order forecasting uses a glass overview with labeled forecast controls, a clearly selected print-format toggle, and a readable result table. Keep numeric forecast settings and editable table quantities outlined, preserve supplier/brand selection and stock/order filters, and contain table scrolling. Forecast formulas, manual overrides, permissions, and A4/thermal output remain unchanged.

On mobile, replace the inset desktop sidebar with the existing drawer behavior. Keep a compact glass topbar, stack panels, and let filter controls wrap. Use no horizontal page overflow; only wide data regions may scroll horizontally.

The login screen shares the pearl/midnight canvas and cobalt accent. Center a single 440px maximum-width glass panel with a 28px radius, store/app identity, a short heading, and clearly labeled fields. On wide screens, a quiet brand illustration area may sit beside the form only when it adds meaning; the baseline design needs no new image assets.

Login refinement: keep branding inside the card and fit the normal layout within the viewport without page scrolling. Use a visibly translucent card (38% white in light mode, 48% smoked navy in dark mode), 28px backdrop blur, and a static blue/teal atmospheric canvas with a subtle glass outline behind the card. Inputs retain a more opaque reading surface. Compact spacing on short screens; allow internal card overflow only when keyboard space, enlarged text, or long errors would otherwise make controls inaccessible. Preserve solid fallbacks and reduced-transparency support.

## Existing frontend foundations

| Area | Current implementation |
| --- | --- |
| Framework | React with Vite and React Router |
| Styling | Tailwind CSS v4 and shared styles in `resources/css/app.css` |
| Theme | `resources/js/context/ThemeContext.jsx`; light/dark presets and server theme settings |
| Dark mode | `.dark` class on the document root; saved `theme_mode` preference |
| Layout | `resources/js/layouts/DashboardLayout.jsx`, Sidebar, and Topbar |
| Glass primitives | `g-card`, `g-section-header`, `g-toolbar`, `g-input`, and `g-btn-*` |
| Icons | Heroicons |
| Charts | Recharts |
| Form widgets | React Select and React Datepicker |
| Notifications | React Hot Toast |
| Typography | Shared OS font stack and role-based screen typography in `resources/css/app.css`; no bundled web font |
| Branding | Store name and logo from `/api/settings`; login uses the Karobar identity |

The dashboard already uses translucent controls, accent gradients, rounded cards, and tabular numbers. Some labels currently use 9–11px text; the target scale below increases readability. The login page uses a separate teal palette (`#639EA0`, `#4A8082`) over a dark gradient (`#2C5364`, `#203A43`, `#0F2027`). Treat that as an existing variation to harmonize during future UI work.

## Typography

Use the operating system's UI font for a familiar appearance and fast rendering. On Apple devices, allow the system font to resolve naturally; do not bundle an Apple font.

```css
--font-ui: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto,
  "Helvetica Neue", Arial, sans-serif;
--font-mono: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas,
  "Liberation Mono", monospace;
```

| Role | Size | Weight | Line height | Use |
| --- | --- | --- | --- | --- |
| Page title | 32px / 2rem | 600 | 1.2 | Main page heading; 26px on mobile |
| Section heading | 20px / 1.25rem | 600 | 1.3 | Major sections and dialogs |
| Card heading | 16px / 1rem | 600 | 1.4 | Charts, tables, and panels |
| Body | 15px / 0.9375rem | 400 | 1.5 | Standard interface copy |
| Input value | 16px / 1rem | 400 | 1.5 | Form fields, including mobile |
| Label / button | 14px / 0.875rem | 500 | 1.4 | Actions and field labels |
| Supporting text | 12px / 0.75rem | 400–500 | 1.5 | Helper text and secondary metadata |
| Table text | 14px / 0.875rem | 400–500 | 1.45 | Rows and values |
| Table heading | 12px / 0.75rem | 600 | 1.4 | Column labels |
| KPI value | 32–36px / 2–2.25rem | 600 | 1.15 | Primary dashboard amounts; 28px on mobile |

All screens share the same font family and role-based sizes and weights. Page titles use 600, body uses 400, and labels/actions use 500; avoid page-specific typography overrides. The shared screen rules in `resources/css/app.css` apply this scale without changing print typography. Product search uses one term and matches any product name, brand name, or supplier name, including partial matches; low-stock filtering still applies to the complete result set.

Use `rem` for implementation so text respects user settings. Keep body text at normal letter spacing; headings may use `-0.02em`. Use uppercase sparingly, with `0.04em` tracking for short category labels. Do not use essential text below 12px.

Use `font-variant-numeric: tabular-nums` for prices, totals, quantities, and chart values. Right-align numeric columns. Reserve monospace for product codes, batch identifiers, and technical references. Preserve the app's `Rs` currency presentation; use a shared formatter and consistent precision. Date labels should be unambiguous, such as `03 Oct 2026`, while machine-facing values may use ISO dates.

## Colors and theme settings

Preserve the existing theme field names. The values below define the **new default palette** for the redesign. Apply new defaults when implementing the design; retain saved custom themes and their intended values. Never overwrite a user's theme records as part of a visual refresh.

| Theme field | Light | Dark | Meaning |
| --- | --- | --- | --- |
| `primary_color` | `#2563EB` | `#60A5FA` | Cobalt primary accent, selected navigation, sales series |
| `primary_hover` | `#1D4ED8` | `#93C5FD` | Primary interaction |
| `primary_light` | `#E8F0FF` | `#172E50` | Primary accent wash |
| `secondary_color` | `#0F766E` | `#2DD4BF` | Teal supporting accent and purchase series |
| `secondary_hover` | `#115E59` | `#5EEAD4` | Secondary interaction |
| `secondary_light` | `#E3F5F1` | `#133632` | Secondary accent wash |
| `tertiary_color` | `#7C3AED` | `#A78BFA` | Additional data series |
| `tertiary_hover` | `#6D28D9` | `#C4B5FD` | Tertiary interaction |
| `tertiary_light` | `#F0EAFF` | `#2D2448` | Tertiary accent wash |
| `background_color` | `#F2F5FA` | `#0B1220` | Pearl / midnight canvas |
| `surface_color` | `#FFFFFF` | `#172234` | Opaque surface and fallback |
| `text_primary` | `#172033` | `#F3F6FC` | Headings and main content |
| `text_secondary` | `#536176` | `#ACB9CE` | Supporting content |
| `success_color` | `#15803D` | `#4ADE80` | Successful actions and healthy stock |
| `warning_color` | `#B45309` | `#FBBF24` | Near expiry and attention required |
| `danger_color` | `#DC2626` | `#F87171` | Errors, destructive actions, critical stock |
| `border_color` | `#D8E1EE` | `#344258` | Structural dividers |
| `shadow_color` | `#1E293B` | `#000000` | Shadow tint |

Accent colors are not automatically safe for small text or button labels. Light primary buttons use cobalt with white text; dark primary buttons use the lighter blue with midnight text. Verify each configured color pairing. Status badges need a readable foreground, tinted background, and a word or icon indicating meaning.

Use neutral colors for most of the screen. Concentrate color in actions, selections, charts, and status indicators. Soft blue/teal background washes should remain static and away from dense data. Avoid moving imagery or vivid gradients behind text.

Existing appearance settings include `button_style`, `sidebar_template`, and `topbar_template`. Current button options map to 8px or 12px radii. Keep compatibility with those settings; any new glass preset or radius options require an explicit implementation and persistence change.

## Glass materials

Use three materials, with stronger opacity as information density increases. Blur values are starting points to validate on actual devices.

| Material | Light fill | Dark fill | Blur | Usage |
| --- | --- | --- | --- | --- |
| Floating glass | `rgba(255,255,255,0.66)` | `rgba(17,27,44,0.76)` | 20px | Topbar, sidebar, floating action groups |
| Content glass | `rgba(255,255,255,0.84)` | `rgba(23,34,52,0.90)` | 12px | KPI cards, chart containers, panels |
| Reading surface | `rgba(255,255,255,0.97)` | `rgba(23,34,52,0.98)` | 0–4px | Tables, forms, menus, dialog content |

**Edges:** Use a 1px border, a subtle inset top highlight, and a soft outer shadow. A light surface may use `rgba(255,255,255,0.75)` for the highlight, with a separate neutral divider where needed. A dark surface may use `rgba(255,255,255,0.12)`. Highlights are decorative; controls still need clearly visible boundaries.

**Depth:** Limit visible glass layering to the app shell and one content layer. Avoid nested blur on every cell, field, or card child. Menus and dialogs should be more opaque than the surface beneath them.

Workspace refinement: navigation uses 44% surface opacity in light mode and 56% in dark mode with 28px blur. Dashboard panels use 56% / 68% with 20px blur and a subtle diagonal highlight. Keep tables and calendars nearly opaque. Dashboard date pickers render in a body portal at overlay layer 40 so glass stacking contexts cannot hide them behind sibling cards.

**Shape:** Suggest fluid glass through rounded edges and a restrained highlight gradient. Keep text undistorted. Complex refraction shaders, continuously moving highlights, and pointer-following effects are outside the baseline system.

Proposed reusable material tokens and CSS recipe, to introduce during implementation:

```css
:root {
  --glass-fill: rgba(255, 255, 255, 0.84);
  --glass-edge: rgba(148, 163, 184, 0.35);
  --glass-highlight: rgba(255, 255, 255, 0.75);
  --glass-fallback: #ffffff;
  --glass-blur: 12px;
  --glass-shadow: 0 8px 28px rgba(30, 41, 59, 0.08);
}

.dark {
  --glass-fill: rgba(23, 34, 52, 0.90);
  --glass-edge: rgba(148, 163, 184, 0.25);
  --glass-highlight: rgba(255, 255, 255, 0.12);
  --glass-fallback: #172234;
  --glass-shadow: 0 8px 28px rgba(0, 0, 0, 0.24);
}

.glass-panel {
  background: var(--glass-fallback);
  border: 1px solid var(--glass-edge);
  border-radius: 20px;
  box-shadow: inset 0 1px 0 var(--glass-highlight), var(--glass-shadow);
}

@supports ((backdrop-filter: blur(1px)) or
           (-webkit-backdrop-filter: blur(1px))) {
  .glass-panel {
    background: var(--glass-fill);
    -webkit-backdrop-filter: blur(var(--glass-blur)) saturate(120%);
    backdrop-filter: blur(var(--glass-blur)) saturate(120%);
  }
}

@media (prefers-reduced-transparency: reduce) {
  .glass-panel {
    background: var(--glass-fallback);
    -webkit-backdrop-filter: none;
    backdrop-filter: none;
  }
}
```

Extend existing `g-*` primitives with these ideas instead of creating competing global component systems. Support an explicit Reduce transparency preference as a future setting, since media-query support varies.

## Spacing, layout, and dimensions

Use a 4px spacing grid: **4, 8, 12, 16, 20, 24, 32, 40, 48px**.

| Element | Target |
| --- | --- |
| Page padding | 16px mobile, 24px tablet, 32px desktop |
| Card padding | 16px mobile, 20–24px desktop |
| Grid gap | 16px mobile, 24px desktop |
| Input and button height | 44px default; 36px only in optional desktop compact mode |
| Touch target | At least 44 × 44px, including icon controls |
| Table row height | About 48px default; 40px in desktop compact mode |
| Sidebar | 248px expanded, 72px collapsed |
| Topbar | 64px minimum; allow wrapping when necessary |
| Card radius | 20px; 24px for prominent summary panels; 28px for login |
| Dialog radius | 24px |
| Input / button radius | 12px target, subject to existing theme options |
| Badge / segmented control | Pill radius where appropriate |
| Dialog width | 480px standard, 720px large; maximum viewport width minus 32px |

Use Tailwind's existing breakpoint conventions: `sm` 640px, `md` 768px, `lg` 1024px, `xl` 1280px, and `2xl` 1536px. Build from mobile upward. Use one-column panels on small screens, two columns where content fits, and four KPI columns on wide dashboards. Keep the dashboard grid flexible rather than forcing a narrow reading width onto data screens.

On mobile, use the existing sidebar as a drawer, stack form fields and filters, and keep important actions visible. Contain wide tables in a labeled horizontal scroll region. Keep identifiers, totals, and actions available; provide a details view for secondary columns. Account for safe-area insets on fixed controls.

## Component behavior

### Navigation and page shell

The topbar and sidebar use floating glass with clear separation from content. Selected navigation uses an accent wash, a readable label, and a stable indicator. Preserve store branding and existing navigation structure. Keep breadcrumbs and page titles on content surfaces, with one clear primary action per page where possible.

Product search dialogs use a frosted frame, borderless search surface, consistent table typography, tinted grouped headings, and an accent edge on the active result. Keep numerical values readable, low-stock warnings visible, and wide results scrolling inside the dialog. Preserve keyboard selection, search criteria, and saved desktop position/size. Compact invoice-table triggers retain their field borders.

### Buttons and controls

Primary actions use an opaque accent fill; secondary actions use a reading surface and border; tertiary actions use a quiet text or icon treatment. Reserve red for destructive actions. Show hover, pressed, focus, loading, and disabled states consistently. During saving, retain the action label and prevent duplicate submission. Icon-only controls need accessible names and tooltips when useful.

### Forms and login

Use persistent labels above fields, 8px label-to-field spacing, and 16–24px between field groups. Values remain readable over a nearly opaque fill. Regular form inputs, textareas, and selects use borderless surfaces with clear contrast against their section. Reserve visible resting borders for small, compact controls and repeated inputs in tables. Preserve focus outlines, invalid-state indicators, and forced-colors boundaries. Show field errors beside the relevant input and preserve entered values after failures. Required and invalid states must have text or symbols as well as color.

The login page may use a more expressive static background and a single glass card. Keep fields and the sign-in action prominent. Harmonize its existing teal variation with the selected brand palette during implementation. Give password visibility controls accessible labels and display authentication errors clearly.

### Tables, invoices, and ledgers

Use a glass outer container with an almost opaque reading surface inside. Sticky headers must hide scrolled content beneath them. Use subtle row separators and distinct hover, selected, and keyboard-focus states. Align amounts to the right, use consistent decimal precision, and emphasize invoice totals with weight and spacing. Place sorting, filters, pagination, and export actions consistently.

Printing and PDF output use opaque backgrounds, readable dark text, and existing receipt/report formats. Remove glass blur, decorative shadows, and motion from printed output.

### Dashboard and charts

Group date filters, KPI summaries, charts, and near-expiry stock into a clear hierarchy. Make KPI values larger than supporting labels. Show currency, date range, and units near the data they describe. Use subtle accent washes for KPI icons rather than filling entire cards with saturated color.

Keep chart plotting areas stable and nearly opaque. Use primary, secondary, and tertiary colors consistently, with readable legends, distinguishable series, and high-opacity tooltips. Pair trends with words or arrows; an increase does not always mean an improvement. Offer a textual summary or data table for essential chart information.

Near-expiry and low-stock indicators use labeled status pills. Preserve product, batch, expiry, quantity, and supplier context. Never rely on a colored dot alone to communicate urgency.

### Dialogs, dropdowns, and notifications

Use a tinted scrim and a highly opaque dialog surface. Trap dialog focus, support Escape where appropriate, and return focus to the trigger on close. Critical confirmations state the affected item and the action explicitly.

Portal React Select menus, date pickers, and tooltips where clipping would occur. Use a shared layer scale: base 0, sticky content 10, navigation 30, dropdown 40, modal scrim 50, modal 60, toast 70, tooltip 80. Portals inside a modal must use that modal's stacking context or a layer above it.

Success toasts may dismiss automatically; actionable errors should remain visible long enough to read and resolve. Use inline messages for field-specific failures.

The notification/demand center is a floating right-side glass panel, inset 16px on desktop and 8px on mobile, with a 440px maximum width and a 28px desktop radius. Render it in a body portal at layer 60, with a tinted blurred scrim behind it. Use a segmented Low stock / Demands selector with counts, readable alert cards, a scrolling list, and a footer that stays visible. Keep dismissal controls separate from product navigation. Refresh updates the selected category; loading failures show a retry state. Support keyboard tabs, focus containment, Escape, and focus restoration when closed.

## Motion

| Interaction | Duration | Treatment |
| --- | --- | --- |
| Hover / color change | 120–160ms | Ease out; adjust tint or border |
| Button press | 100ms | Optional scale to 0.98 |
| Menu / tooltip | 160–200ms | Fade and translate up to 4px |
| Drawer / dialog | 220–280ms | Ease out; restrained slide or fade |
| Theme change | Up to 200ms | Limit to visible surfaces and controls |

Use `cubic-bezier(0.2, 0.8, 0.2, 1)` for entrances. Avoid `transition: all`, animated blur, large hover lifts, and continuously animated decorative backgrounds. Honor `prefers-reduced-motion` by removing decorative movement and using static or minimal feedback. Loading states must remain understandable without animation.

## Accessibility and performance

- Target WCAG AA contrast: 4.5:1 for normal text, 3:1 for large text, and 3:1 for meaningful control boundaries and indicators. Measure against the composited glass background in both modes.
- Keep visible keyboard focus, semantic labels, logical tab order, and keyboard access to navigation, tables, menus, and dialogs.
- Support zoom to 200% without losing actions or content. Avoid fixed-height text containers that clip translated or enlarged labels.
- Provide solid surfaces when blur is unsupported or transparency is reduced. Increased-contrast and forced-colors modes must preserve borders and focus indicators.
- Apply backdrop blur to a few bounded surfaces. Avoid persistent `will-change`, blanket GPU promotion, or nested blur without profiling evidence.
- Existing `g-card` containment can clip menus or create stacking contexts. Verify overlays, sticky elements, and scrolling when changing these styles.
- Verify responsiveness and scrolling on a lower-powered device. Reduce blur before compromising interaction responsiveness.

## Implementation sequence and review

1. Centralize typography and semantic color/material tokens in `resources/css/app.css`, retaining ThemeContext field compatibility.
2. Update shared `g-*` primitives, then Sidebar and Topbar, including solid fallbacks and focus states.
3. Apply the system to the dashboard: larger labels and KPI values, consistent spacing, readable chart and table surfaces.
4. Extend the same controls and surfaces to login, inventory, invoices, ledgers, reports, and settings.
5. Verify light/dark themes, custom theme colors, keyboard use, mobile layouts, reduced motion/transparency, dropdown layering, and print output.

For each screen, confirm that the primary task is obvious, labels and amounts remain readable over glass, component styles come from shared tokens, all interaction states exist, and the UI stays responsive. Visual changes should preserve existing business behavior and user theme preferences.

Row edit and delete actions use the shared 44px icon buttons (`PencilSquareIcon` and `TrashIcon`), with item-specific accessible labels and tooltips. Preserve disabled states and show the reason deletion is blocked.
