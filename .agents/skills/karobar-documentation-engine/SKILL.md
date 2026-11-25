---
name: karobar-documentation-engine
description: >-
  Source-code-verified bilingual (English and Urdu) end-user documentation
  engine for Karobar-App ERP v2. Use when auditing user-facing features,
  writing end-user module manuals, daily business how-to guides, role-based
  employee training, FAQs/troubleshooting, or verifying documentation against
  actual application behavior and screenshots.
---

# Karobar-App End-User Documentation Engine

This skill governs the **bilingual (English + Urdu) end-user documentation** for **Karobar-App** (SofTech Web Solutions), designed for **pharmacy owners, medical store owners, cashiers, salesmen, inventory managers, accountants, and business administrators**.

> **Core Mission:** Every user-facing page must answer:
> 1. What can I do here?
> 2. Why would I use this feature?
> 3. How do I use it?
> 4. What information do I need to enter?
> 5. What happens after I click the button?
> 6. What should I do if something goes wrong?
> 7. How does this feature help my daily business?

---

## Strict Scope & Audience Rules

1. **No Developer Jargon in User Docs:** Never put `.NET` architecture, React components, Laravel controllers, SQLite schemas, API endpoints, dependency injection, source-code implementation, automated test code, or developer debugging inside `docs/en/` or `docs/ur/`.
2. **Keep Internal Material Separate:** Internal developer/QA/audit files (`docs/PROJECT_ARCHITECTURE.md`, `docs/MODULE_INVENTORY.md`, `docs/qa/`, etc.) stay separate and are **outside** the end-user documentation deliverable.
3. **Verify Against Actual Code (Silently):** Before documenting any screen, button, field, calculation, or permission, inspect the actual React pages (`resources/js/pages/`), Formula files (`resources/js/Formula/`), and backend controllers/services (`native-win/src/`) so you never invent features—then translate that verified behavior into **plain, practical business language**.
4. **Business Calculations Without Code:** Explain Purchase total, Selling total, Discount, Profit, Profit margin, Free/Bonus quantities, Stock balance, and Outstanding payments using simple step-by-step numerical examples (*what you enter → how it is calculated → what the result means*).
5. **Real Screenshots Only:** Use only verified screenshots from `Screenshots/*.webp` with numbered interface explanations, tables, warnings, and tips. Never invent screenshots.
6. **Bilingual Parity (EN + UR):** Every document in `docs/en/` must have an equivalent in `docs/ur/` in natural **Pakistani Urdu script** (not Roman Urdu), keeping common English pharmacy/POS terms in parentheses where helpful (e.g., *منافع کا تناسب (Profit Margin)*).
7. **Mark Unverified Behavior:** If a feature or edge case cannot be confirmed from the application, mark it `UNVERIFIED` or omit from claiming it works. Do not modify application source code.

---

## Supported Commands

| Command | Action |
| :--- | :--- |
| `/karobar-documentation-engine` (or `audit`) | Audit user-facing screens, routes, screenshots (`Screenshots/*.webp`), and existing `docs/en/` + `docs/ur/` coverage; update `docs/DOCUMENTATION_STATUS.md`. |
| `/karobar-documentation-engine module <name>` | Inspect actual UI/formulas/behavior for `<name>`, write `docs/en/modules/<slug>.md` and `docs/ur/modules/<slug>.md` using the **A–I User Manual Template**, embed matching `Screenshots/*.webp`, and update `docs/DOCUMENTATION_STATUS.md`. |
| `/karobar-documentation-engine workflow <name>` | Write a practical business how-to guide in `docs/en/workflows/<slug>.md` and `docs/ur/workflows/<slug>.md`. |
| `/karobar-documentation-engine training <role>` | Write role-based training lessons (objectives, instructions, examples, practice exercises) in `docs/en/training/` and `docs/ur/training/`. |
| `/karobar-documentation-engine verify` | Verify `docs/en/` and `docs/ur/` against actual UI screens, formulas, permissions, screenshots, EN/UR parity, and internal links; update `docs/DOCUMENTATION_STATUS.md`. |
| `/karobar-documentation-engine resume` | Read `docs/DOCUMENTATION_STATUS.md`, pick the next `Undocumented` or `Partial` module/guide/lesson, and execute it incrementally. |

---

## Layer 1: Module User Manual Template (Sections A–I)

Every module file in `docs/en/modules/<slug>.md` and `docs/ur/modules/<slug>.md` must follow:

- **A. Purpose and Overview** — What this module is, why a pharmacy/medical store needs it, and how it helps daily business.
- **B. What Users Can Do** — Bullet list of capabilities and which staff roles typically use them.
- **C. Screen-by-Screen Explanation** — Embed actual `Screenshots/*.webp` with numbered callouts explaining each area of the screen.
- **D. Every Field, Button, Tab and Option** — Clear tables of all inputs, dropdowns, filters, tabs, and action buttons (what to enter and what happens when clicked).
- **E. Step-by-Step Instructions** — Numbered, beginner-friendly instructions for every task (create, edit, search, print, export, void/return).
- **F. Practical Business Examples** — Realistic Pakistani pharmacy examples (including simple calculation breakdowns where applicable).
- **G. Expected Results** — What changes in stock, cash drawer, customer/supplier Khata, and reports after completing the action.
- **H. Common Mistakes and Solutions** — Practical troubleshooting table for staff errors.
- **I. Related Features and Workflows** — Links to connected modules and how-to guides.

---

## End-User Documentation Structure

```text
docs/
  README.md                    # Central Bilingual End-User Portal (EN + UR)
  DOCUMENTATION_STATUS.md      # End-User Coverage & Verification Tracker
  en/
    README.md                  # English User Documentation Home
    modules/                   # Layer 1: Complete User Manual (A–I per module)
    workflows/                 # Layer 2: Daily Business How-To Guides
    training/                  # Layer 3: Role-Based Employee Training
    faq/                       # Layer 4: FAQ & Troubleshooting
  ur/
    README.md                  # اردو صارف دستاویزات ہوم
    modules/                   # لیول 1: مکمل یوزر مینوئل (A–I)
    workflows/                 # لیول 2: روزانہ کاروباری رہنمائی
    training/                  # لیول 3: اسٹاف ٹریننگ اسباق
    faq/                       # لیول 4: عام سوالات اور مسائل کا حل
```
