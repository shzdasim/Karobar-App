# Karobar App — Rules for AI and Contributors

Read this file and [Design.md](Design.md) before changing the frontend. These rules apply to maintenance, redesigns, bug fixes, and feature work in this repository. They guide implementation; they do not claim that the target design is already present everywhere.

## Authority and scope

- Follow the user's current request and applicable higher-priority instructions. Use these repository rules for choices the user has not explicitly changed.
- `Design.md` is the source for visual direction, typography, palette, materials, spacing, motion, and component behavior. This file defines change boundaries and verification expectations.
- Existing code is the source for business behavior and contracts until a requested change explicitly alters them. Do not infer that a redesign authorizes functional changes.
- Proceed with routine, reversible work within the requested scope. Ask only when missing information or a consequential decision actually blocks the task. Explain conflicts with existing rules clearly.
- Keep edits focused. Preserve unrelated user changes. Do not perform broad cleanup, dependency upgrades, repository resets, or unrelated refactors during design work.

## Preserve the design system

1. Use the modern glass direction in `Design.md`: pearl/midnight canvas, floating frosted navigation, cobalt actions, teal supporting accents, readable interiors, and restrained depth.
2. Reuse shared components and the existing `g-*` primitives before introducing another styling system. Centralize reusable tokens in `resources/css/app.css` and integrate them with `resources/js/context/ThemeContext.jsx`.
3. Use semantic color tokens in components. Hardcoded palette values belong in centralized presets or material definitions, not repeated page-specific styles. Dynamic chart colors may come from the active theme.
4. Use the documented font scale, 4px spacing grid, radii, and layer scale. Do not invent a new palette, font, card style, or animation for each page. Use the shared screen typography: the same role must have the same font family, size, and weight across pages. Keep print typography independent.
5. Support both light and dark modes. Preserve saved custom theme values, branding, `theme_mode`, and existing theme field names. New defaults must not overwrite existing theme records.
6. Keep glass strongest in navigation and more opaque behind forms, tables, menus, and financial data. Provide a solid fallback. Never reduce legibility to make the glass effect stronger.
7. Keep essential text at least 12px, numeric data aligned and readable, touch controls usable, and keyboard focus visible. Include reduced-motion behavior for new motion.
8. Prefer local component classes to global selectors. Review every shared style change for its effects on other screens, dialogs, dropdowns, sticky headers, and print views.
9. Preserve layouts on mobile and at enlarged text sizes. Contain wide-table scrolling; do not hide essential data or actions solely to simplify a layout.
10. If the user requests a lasting visual exception, implement it within scope and update `Design.md` to describe the decision. Do not silently rewrite these rules to justify a change.

## Protect business behavior

For UI-only work, preserve the following unless the user explicitly requests a related functional change:

| Protected area | Preserve |
| --- | --- |
| Authentication | Login/logout, token handling, expiry, remember-me behavior, session cleanup, password reset |
| Authorization | Roles, permissions, route guards, server checks, and action visibility |
| Licensing | Activation, machine checks, license status, expiry, and server enforcement |
| Financial calculations | Prices, quantities, discounts, tax, rounding, payments, balances, returns, totals, and currency precision |
| Inventory | Stock movements, batches, expiry logic, product identifiers, and purchase/sale links |
| API contracts | Endpoints, HTTP methods, payload fields, response interpretation, validation, and error handling |
| Persistence | Database schema, records, migrations, stored preferences, and storage keys |
| Operational workflows | Posting invoices, returns, imports, exports, backups, restores, and ledger updates |
| Output | Receipt dimensions, barcodes, PDF formats, print layouts, and report content |

Do not remove a permission or license check to make a component render. Do not convert failed API requests into successful results. Do not replace live data with fabricated production values, silently swallow failures, or change a calculation while adjusting how it is displayed.

Display formatting may improve readability but must retain the underlying value and established precision. A change to amount precision, date interpretation, report filters, or business terminology needs to be part of the requested functional scope.

A related, confirmed defect may be fixed when the task authorizes fixes. Describe the behavioral change and verify it separately from cosmetic work. Report unrelated defects without silently expanding the task.

## Protect the repository and user data

- Inspect relevant files and repository status before editing. Preserve work already present; never overwrite it merely to obtain a clean diff.
- Do not modify secrets, environment configuration, production data, lockfiles, or migrations for a cosmetic change.
- Use the project's existing React, Tailwind, Heroicons, chart, and form libraries. Add a dependency only when the requested feature requires it and existing tools cannot reasonably provide it.
- Do not expose credentials, tokens, customer data, or private configuration in logs, examples, screenshots, documentation, or commits.
- Do not commit, push, deploy, publish, or perform destructive operations unless authorized by the user's task. Documentation rules are not permission to perform those actions.
- Do not introduce an alternate authentication, theming, routing, or state architecture during a visual refresh.

## Expected workflow

1. Read `Design.md`, this file, and applicable repository instructions. Inspect the relevant component, shared styles, theme configuration, and dependencies.
2. Identify whether the change is presentation-only or intentionally changes behavior. Trace shared dependencies before editing a global primitive.
3. Implement the smallest coherent change. Prefer migrating a shared component and its affected consumers together over creating duplicate page-specific patterns.
4. Preserve loading, empty, error, disabled, hover, focus, selected, and success states. Do not ship only the populated happy-path view.
5. Validate according to the change matrix below. Address failures caused by the change. Identify pre-existing failures and unavailable checks honestly.
6. Review the diff for unrelated edits, overwritten preferences, hardcoded styles, removed guards, changed payloads, altered calculations, and accidental print changes.
7. Report what changed, how it was verified, and any remaining limitation. Clearly distinguish a documented design from an implemented and visually reviewed UI.

## Validation appropriate to the change

| Change | Expected checks |
| --- | --- |
| Documentation only | Check Markdown structure, links, consistency between both files, and `git diff --check` |
| Frontend presentation | Run `npm run build`; inspect affected screens and their states when a browser is available |
| Shared theme/layout/styles | Build; review representative dashboard, data-entry, table, dropdown, modal, and login views in both modes |
| Functional frontend/backend | Relevant regression tests and appropriate build checks; preserve API and business invariants |
| Print/report presentation | Inspect the affected print/PDF output as well as the on-screen view |

Browser review is required for every UI change before reporting it complete. Inspect the actual affected screen; build success alone is not visual validation. If the browser is unavailable, report that limitation explicitly.

For visual changes, check a narrow mobile viewport, a desktop viewport, keyboard navigation, enlarged text, contrast, and overlay positioning. Check custom themes when touching theme integration. Do not claim browser or device testing that was not performed.

Do not add tests that only repeat implementation details for a low-impact style or documentation edit. Add meaningful regression coverage for changed behavior, especially authentication, permissions, calculations, and persistence.

## Making these rules discoverable

`rule.md` is a contributor reference; its filename alone does not guarantee that an AI tool automatically loads it. AI tasks should explicitly read it. If an `AGENTS.md` or another tool-specific repository instruction file is introduced, link to this file and `Design.md` rather than copying their contents into divergent versions.

Keep `Design.md` and `rule.md` consistent when the user changes the design direction. Record real decisions rather than inventing requirements, adding unnecessary approval steps, or claiming rules can guarantee that no future regression occurs.
