// src/components/Topbar.jsx
import React, { useEffect, useRef, useState, useMemo } from "react";
import { useNavigate, useLocation } from "react-router-dom";
import { useAuth } from "../context/AuthContext.jsx";
import { ClipboardDocumentListIcon, ShoppingCartIcon, ClockIcon, BellIcon, Bars3Icon, BanknotesIcon, BuildingLibraryIcon, MagnifyingGlassIcon, CommandLineIcon } from "@heroicons/react/24/solid";
import { useLicense } from "@/context/LicenseContext.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";
import { useCurrency } from "@/context/CurrencyContext.jsx";
import ProductSearchModal from "@/components/ProductSearchModal.jsx";
import KeyboardShortcutsModal from "@/components/KeyboardShortcutsModal.jsx";
import ThemeToggle from "./ThemeToggle.jsx";
import NotificationCenter from "./NotificationCenter.jsx";
import CashDrawerModal from "./CashDrawerModal.jsx";
import AccountsModal from "./AccountsModal.jsx";
import axios from "axios";

function formatRemaining(ms) {
  if (ms == null) return "Perpetual";
  const s = Math.floor(ms / 1000);
  const d = Math.floor(s / 86400);
  const h = Math.floor((s % 86400) / 3600);
  const m = Math.floor((s % 3600) / 60);
  const sec = s % 60;
  if (d > 0) return `${d}d ${h}h ${m}m`;
  if (h > 0) return `${h}h ${m}m ${sec}s`;
  if (m > 0) return `${m}m ${sec}s`;
  return `${sec}s`;
}

export default function Topbar({ pageTitle, navigationStyle = "sidebar", onMobileMenuClick }) {
  const [open, setOpen] = useState(false);
  const menuRef = useRef(null);
  const btnRef = useRef(null);

  // Notification center state
  const [notifOpen, setNotifOpen] = useState(false);
  const [notifCount, setNotifCount] = useState(0);

  const navigate = useNavigate();
  const location = useLocation();
  const { user, logout } = useAuth();
  const { theme } = useTheme();
  const { formatCurrency } = useCurrency();

  // Cash Drawer Register state
  const [drawerModalOpen, setDrawerModalOpen] = useState(false);
  const [drawerStatus, setDrawerStatus] = useState({ isOpen: false, expected: 0, loading: true });

  // Financial Accounts state
  const [accountsModalOpen, setAccountsModalOpen] = useState(false);
  const [accountsStatus, setAccountsStatus] = useState({ total_balance: 0, loading: true });

  // Product Search Modal state (opens anywhere with Alt+P)
  const [productSearchModalOpen, setProductSearchModalOpen] = useState(false);

  // Keyboard Shortcuts Modal state (opens anywhere with Alt+K or ?)
  const [shortcutsModalOpen, setShortcutsModalOpen] = useState(false);

  const fetchDrawerStatus = async () => {
    try {
      const res = await axios.get("/api/cash-drawer/current");
      setDrawerStatus({
        isOpen: !!res.data?.is_open,
        expected: Number(res.data?.summary?.expected_balance || 0),
        loading: false,
      });
    } catch {
      setDrawerStatus((prev) => ({ ...prev, loading: false }));
    }
  };

  const fetchAccountsStatus = async () => {
    try {
      const res = await axios.get("/api/accounts");
      setAccountsStatus({
        total_balance: Number(res.data?.total_balance || 0),
        loading: false,
      });
    } catch {
      setAccountsStatus((prev) => ({ ...prev, loading: false }));
    }
  };

  useEffect(() => {
    fetchDrawerStatus();
    fetchAccountsStatus();
    const onDrawerChanged = () => {
      fetchDrawerStatus();
      fetchAccountsStatus();
    };
    const onAccountsChanged = () => {
      fetchAccountsStatus();
      fetchDrawerStatus();
    };
    const onStorage = (e) => {
      if (e.key === "cash_drawer_sync" || e.key === "accounts_sync") {
        fetchDrawerStatus();
        fetchAccountsStatus();
      }
    };
    window.addEventListener("cash-drawer-changed", onDrawerChanged);
    window.addEventListener("accounts-changed", onAccountsChanged);
    window.addEventListener("storage", onStorage);
    return () => {
      window.removeEventListener("cash-drawer-changed", onDrawerChanged);
      window.removeEventListener("accounts-changed", onAccountsChanged);
      window.removeEventListener("storage", onStorage);
    };
  }, []);

  // Also auto-refresh drawer and accounts balance on navigation (e.g. after invoice creation)
  useEffect(() => {
    fetchDrawerStatus();
    fetchAccountsStatus();
  }, [location.pathname]);

// Poll for low-stock + user-demand notification count
  useEffect(() => {
    let cancelled = false;
    const fetchCount = async () => {
      try {
        const [lowStockRes, demandsRes] = await Promise.allSettled([
          axios.get("/api/notifications/low-stock", { params: { limit: 1 } }),
          axios.get("/api/user-demands", { params: { status: "pending", limit: 1 } }),
        ]);
        if (cancelled) return;
        const lowStockCount = lowStockRes.status === "fulfilled" ? Number(lowStockRes.value.data?.count || 0) : 0;
        const demandCount = demandsRes.status === "fulfilled" ? Number(demandsRes.value.data?.pending_count || 0) : 0;
        setNotifCount(lowStockCount + demandCount);
      } catch {
        if (!cancelled) setNotifCount(0);
      }
    };
    fetchCount();
    const interval = setInterval(fetchCount, 60000); // every 60s

    // Refresh immediately when notifications are dismissed elsewhere
    const onChanged = () => fetchCount();
    window.addEventListener("notifications-changed", onChanged);

    return () => {
      cancelled = true;
      clearInterval(interval);
      window.removeEventListener("notifications-changed", onChanged);
    };
  }, []);

  // License status
  const { loading: licLoading, valid: licValid, remainingMs } = useLicense();
  const leftTxt = formatRemaining(remainingMs);

  // Get theme colors with defaults
  const themeColors = useMemo(() => ({
    primary: theme?.primary_color || '#3b82f6',
    primaryHover: theme?.primary_hover || '#2563eb',
    primaryLight: theme?.primary_light || '#dbeafe',
    secondary: theme?.secondary_color || '#8b5cf6',
    secondaryHover: theme?.secondary_hover || '#7c3aed',
    secondaryLight: theme?.secondary_light || '#ede9fe',
    tertiary: theme?.tertiary_color || '#06b6d4',
    tertiaryHover: theme?.tertiary_hover || '#0891b2',
    tertiaryLight: theme?.tertiary_light || '#cffafe',
  }), [theme]);

  // Get button style class from theme
  const buttonStyle = theme?.button_style || 'modern';
  const isOutlined = buttonStyle === 'outlined';
  const buttonStyleClass = {
    'modern':     'rounded-xl',
    'rounded':    'rounded-lg',
    'rounded-sm': 'rounded-md',
    'outlined':   'rounded-lg',
    'soft':       'rounded-2xl',
    'pill':       'rounded-full',
  }[buttonStyle] || 'rounded-xl';

  // Get button background based on style (outlined = transparent)
  const getButtonBg = (color) => isOutlined ? "transparent" : `color-mix(in srgb, ${color} 8%, var(--workspace-surface))`;
  const getButtonBorder = (color) => `1px solid color-mix(in srgb, ${color} ${isOutlined ? "50%" : "18%"}, transparent)`;
  const getButtonTextColor = () => "var(--workspace-ink)";

  // Dynamic button class generator
  const getButtonClass = (baseColor, hoverColor, shadowColor) => 
    `inline-flex items-center gap-1.5 ${buttonStyleClass} px-3 py-1 text-xs font-semibold transition-colors focus:outline-hidden focus:ring-2 focus:ring-${baseColor}/50 relative z-50 ${isOutlined ? 'border-2' : 'shadow-xs'}`;

  const openInNewTab = (path) => window.open(path, "_blank", "noopener,noreferrer");

  useEffect(() => {
    const onKeyDown = (e) => {
      const tag = (e.target?.tagName || "").toLowerCase();
      const isTyping =
        ["input", "textarea", "select"].includes(tag) || e.target?.isContentEditable;

      // ? (Shift+/) opens Shortcuts Modal when not typing
      if (!isTyping && e.key === "?") {
        e.preventDefault();
        setShortcutsModalOpen(true);
        return;
      }

      if (!e.altKey) return;

      // Alt+P opens Product Search Modal anywhere in the software (even when focused in forms)
      if (e.code === "KeyP" || e.key.toLowerCase() === "p") {
        e.preventDefault();
        setProductSearchModalOpen((v) => !v);
        return;
      }

      // Alt+K opens Keyboard Shortcuts modal anywhere in the software
      if (e.code === "KeyK" || e.key.toLowerCase() === "k") {
        e.preventDefault();
        setShortcutsModalOpen((v) => !v);
        return;
      }

      // Alt+D opens Cash Drawer modal anywhere
      if (e.code === "KeyD" || e.key.toLowerCase() === "d") {
        e.preventDefault();
        setDrawerModalOpen((v) => !v);
        return;
      }

      // Alt+A opens Accounts Register modal anywhere
      if (e.code === "KeyA" || e.key.toLowerCase() === "a") {
        e.preventDefault();
        setAccountsModalOpen((v) => !v);
        return;
      }

      if (isTyping) return;

      if (e.code === "Digit1" || e.code === "Numpad1") {
        e.preventDefault();
        openInNewTab("/purchase-invoices/create");
      } else if (e.code === "Digit2" || e.code === "Numpad2") {
        e.preventDefault();
        openInNewTab("/sale-invoices/create/retail");
      } else if (e.key.toLowerCase() === "w") {
        e.preventDefault();
        openInNewTab("/sale-invoices/create/wholesale");
      }
    };

    const onOpenSearch = () => setProductSearchModalOpen(true);
    const onOpenShortcuts = () => setShortcutsModalOpen(true);

    window.addEventListener("keydown", onKeyDown);
    window.addEventListener("open-product-search", onOpenSearch);
    window.addEventListener("open-shortcuts-modal", onOpenShortcuts);
    return () => {
      window.removeEventListener("keydown", onKeyDown);
      window.removeEventListener("open-product-search", onOpenSearch);
      window.removeEventListener("open-shortcuts-modal", onOpenShortcuts);
    };
  }, []);

  useEffect(() => {
    function onClick(e) {
      if (!open) return;
      if (
        menuRef.current &&
        !menuRef.current.contains(e.target) &&
        btnRef.current &&
        !btnRef.current.contains(e.target)
      ) {
        setOpen(false);
      }
    }
    function onEsc(e) {
      if (e.key === "Escape") setOpen(false);
    }
    window.addEventListener("mousedown", onClick);
    window.addEventListener("keydown", onEsc);
    return () => {
      window.removeEventListener("mousedown", onClick);
      window.removeEventListener("keydown", onEsc);
    };
  }, [open]);

  return (
    <div className="workspace-topbar-slot">
      <header
        role="banner"
        className="workspace-topbar"
      >
        <div className="workspace-topbar-row">
          {/* Left: Mobile hamburger + Page Title */}
          <div className="flex items-center gap-2.5 min-w-0 shrink">
            {/* Mobile menu button — visible on < lg */}
            <div className="lg:hidden shrink-0">
              <button
                type="button"
                onClick={onMobileMenuClick}
                aria-label="Open sidebar"
                className="inline-flex items-center justify-center w-9 h-9 rounded-xl bg-white/70 dark:bg-slate-800/70 border border-gray-200/70 dark:border-slate-600/70 text-gray-700 dark:text-gray-200 hover:bg-gray-100 dark:hover:bg-slate-700 focus:outline-none transition-all duration-150"
              >
                <Bars3Icon className="w-5 h-5" />
              </button>
            </div>

            {pageTitle && (
              <h1 className="text-sm sm:text-base font-bold text-slate-900 dark:text-slate-100 truncate tracking-tight">
                {pageTitle}
              </h1>
            )}
          </div>

          {/* Right: Quick actions + Balance badges + Utilities + User (Single line fit) */}
          <div className="workspace-topbar-actions">
            {/* Action: Purchase */}
            <button
              type="button"
              onClick={() => openInNewTab("/purchase-invoices/create")}
              aria-label="Open purchase invoice"
              aria-keyshortcuts="Alt+1"
              title="Open Purchase Invoice (Alt+1) in a new tab"
              className="inline-flex items-center gap-1.5 h-9 px-2.5 sm:px-3 text-xs font-semibold theme-btn-secondary relative z-20 shrink-0"
            >
              <ClipboardDocumentListIcon className="w-4 h-4 shrink-0" />
              <span className="hidden sm:inline">Purchase</span>
            </button>

            {/* Action: Sale */}
            <button
              type="button"
              onClick={() => openInNewTab("/sale-invoices/create/retail")}
              aria-label="Open sale invoice"
              aria-keyshortcuts="Alt+2"
              title="Open Sale Invoice (Alt+2) in a new tab"
              className="inline-flex items-center gap-1.5 h-9 px-2.5 sm:px-3 text-xs font-semibold theme-btn-primary relative z-20 shrink-0"
            >
              <ShoppingCartIcon className="w-4 h-4 shrink-0" />
              <span className="hidden sm:inline">Sale</span>
            </button>

            {/* Cash Drawer register button */}
            <button
              type="button"
              onClick={() => setDrawerModalOpen(true)}
              aria-label="Cash Drawer register"
              title={
                drawerStatus.isOpen
                  ? `Cash Drawer Open: ${formatCurrency(drawerStatus.expected)}`
                  : "Cash Drawer is Closed (Click to Open Shift)"
              }
              className={`inline-flex items-center gap-1.5 h-9 px-2.5 sm:px-3 text-xs font-semibold rounded-xl transition-all duration-150 relative z-20 shrink-0 border ${
                drawerStatus.isOpen
                  ? "bg-emerald-500/10 border-emerald-500/30 text-emerald-600 dark:text-emerald-400 hover:bg-emerald-500/20"
                  : "bg-slate-500/10 border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-500/20"
              }`}
            >
              <BanknotesIcon className="w-4 h-4 shrink-0" />
              <span className="flex items-center gap-1.5">
                <span
                  className={`w-2 h-2 rounded-full ${
                    drawerStatus.isOpen ? "bg-emerald-500 animate-pulse" : "bg-amber-400"
                  }`}
                />
                <span className="hidden md:inline font-bold">
                  {drawerStatus.isOpen ? formatCurrency(drawerStatus.expected) : "Drawer"}
                </span>
              </span>
            </button>

            {/* Financial Accounts register button */}
            <button
              type="button"
              onClick={() => setAccountsModalOpen(true)}
              aria-label="Financial Accounts register"
              title={`Financial Accounts Register: ${formatCurrency(accountsStatus.total_balance)} across all accounts`}
              className="inline-flex items-center gap-1.5 h-9 px-2.5 sm:px-3 text-xs font-semibold rounded-xl transition-all duration-150 relative z-20 shrink-0 border bg-blue-500/10 border-blue-500/30 text-blue-600 dark:text-blue-400 hover:bg-blue-500/20"
            >
              <BuildingLibraryIcon className="w-4 h-4 shrink-0" />
              <span className="flex items-center gap-1.5">
                <span className="w-2 h-2 rounded-full bg-blue-500" />
                <span className="hidden md:inline font-bold">
                  {formatCurrency(accountsStatus.total_balance)}
                </span>
              </span>
            </button>

            <div className="mx-0.5 h-5 w-px bg-slate-300/60 dark:bg-slate-700/60 shrink-0" />

            {/* Theme Toggle */}
            <div className="shrink-0 flex items-center">
              <ThemeToggle />
            </div>

            {/* Keyboard Shortcuts Guide button */}
            <button
              type="button"
              onClick={() => setShortcutsModalOpen(true)}
              title="Keyboard Shortcuts (Alt+K or ?)"
              aria-label="Open keyboard shortcuts guide"
              aria-keyshortcuts="Alt+K"
              className={`relative inline-flex items-center justify-center h-9 w-9 ${buttonStyleClass} transition-colors focus:outline-hidden focus:ring-2 focus:ring-${themeColors.primary}/50 shrink-0 ${isOutlined ? 'border-2' : 'shadow-xs'}`}
              style={{
                background: getButtonBg(themeColors.primary),
                border: getButtonBorder(themeColors.primary),
                color: getButtonTextColor(themeColors.primary),
                borderRadius: 'var(--btn-radius, 0.75rem)',
              }}
            >
              <CommandLineIcon className="w-4 h-4 shrink-0" />
            </button>

            {/* Notification Bell */}
            <button
              type="button"
              onClick={() => setNotifOpen(true)}
              title="Open Notification Center"
              aria-label="Open notification center"
              className={`relative inline-flex items-center justify-center h-9 w-9 ${buttonStyleClass} transition-colors focus:outline-hidden focus:ring-2 focus:ring-${themeColors.primary}/50 shrink-0 ${isOutlined ? 'border-2' : 'shadow-xs'}`}
              style={{
                background: getButtonBg(themeColors.primary),
                border: getButtonBorder(themeColors.primary),
                color: getButtonTextColor(themeColors.primary),
                borderRadius: 'var(--btn-radius, 0.75rem)',
              }}
            >
              <BellIcon className="w-4 h-4 shrink-0" />
              {notifCount > 0 && (
                <span
                  className="absolute -top-1 -right-1 inline-flex items-center justify-center min-w-[16px] h-[16px] px-1 rounded-full text-[9px] font-bold text-white"
                  style={{
                    background: 'linear-gradient(to bottom right, #ef4444, #dc2626)',
                    boxShadow: '0 2px 6px rgba(239,68,68,0.5)',
                  }}
                >
                  {notifCount > 99 ? "99+" : notifCount}
                </span>
              )}
            </button>

            {/* License badge */}
            <button
              type="button"
              onClick={() => navigate("/settings#license")}
              className="inline-flex items-center gap-1.5 h-9 px-2.5 text-xs font-semibold theme-btn-tertiary relative z-20 shrink-0"
              title={licValid ? "License is active" : "License required – click to activate"}
            >
              <ClockIcon className="w-3.5 h-3.5 shrink-0" />
              <span className="hidden xl:inline">{licLoading ? "Checking…" : licValid ? `${leftTxt} left` : "Activate"}</span>
            </button>

            <div className="mx-0.5 h-5 w-px bg-slate-300/60 dark:bg-slate-700/60 shrink-0" />

            {/* User menu */}
            <div className="relative shrink-0">
              <button
                ref={btnRef}
                type="button"
                onClick={() => setOpen((v) => !v)}
                className="inline-flex items-center gap-1.5 h-9 px-2.5 text-xs font-semibold theme-btn-tertiary relative z-20 shrink-0"
                aria-haspopup="menu"
                aria-expanded={open}
                aria-controls="topbar-user-menu"
              >
                <span className="inline-grid h-5 w-5 place-items-center rounded-full border border-current/30 text-xs font-bold shrink-0">
                  {(user?.name || "U").slice(0, 1).toUpperCase()}
                </span>
                <span className="hidden xl:inline text-xs font-semibold workspace-user-name">
                  {user?.name || "User"}
                </span>
                <svg className="w-3.5 h-3.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                </svg>
              </button>

              {open && (
                <div
                  id="topbar-user-menu"
                  ref={menuRef}
                  role="menu"
                  className={[
                    "absolute right-0 mt-2 w-48 rounded-xl overflow-hidden shadow-xl border border-slate-200 dark:border-slate-700",
                    "workspace-user-menu",
                    navigationStyle === 'topbar' ? 'z-1000000' : 'z-50',
                  ].join(" ")}
                >
                  <button
                    role="menuitem"
                    onClick={() => {
                      setOpen(false);
                      navigate("/profile");
                    }}
                    className="workspace-user-menu-item"
                  >
                    Profile
                  </button>
                  <button
                    role="menuitem"
                    onClick={() => {
                      setOpen(false);
                      fetch("/api/logout", {
                        method: "POST",
                        headers: { Authorization: `Bearer ${localStorage.getItem("token")}` },
                      }).finally(() => {
                        logout();
                        navigate("/");
                      });
                    }}
                    className="workspace-user-menu-item"
                  >
                    Logout
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>
      </header>

      {/* Notification Center side panel */}
      <NotificationCenter
        open={notifOpen}
        onClose={() => setNotifOpen(false)}
      />

      {/* Cash Drawer Shift Modal */}
      <CashDrawerModal
        isOpen={drawerModalOpen}
        onClose={() => setDrawerModalOpen(false)}
        onSessionUpdated={fetchDrawerStatus}
      />

      {/* Financial Accounts Modal */}
      <AccountsModal
        isOpen={accountsModalOpen}
        onClose={() => setAccountsModalOpen(false)}
        onAccountUpdated={() => {
          fetchAccountsStatus();
          fetchDrawerStatus();
        }}
        onOpenCashDrawer={() => setDrawerModalOpen(true)}
      />

      {/* Product Search Modal (Alt+P anywhere) */}
      <ProductSearchModal
        isOpen={productSearchModalOpen}
        onClose={() => setProductSearchModalOpen(false)}
      />

      {/* Keyboard Shortcuts Modal (Alt+K or ?) */}
      <KeyboardShortcutsModal
        isOpen={shortcutsModalOpen}
        onClose={() => setShortcutsModalOpen(false)}
      />
    </div>
  );
}

