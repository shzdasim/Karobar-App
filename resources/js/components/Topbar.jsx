// src/components/Topbar.jsx
import React, { useEffect, useRef, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext.jsx";
import { ClipboardDocumentListIcon, ShoppingCartIcon, ClockIcon, BellIcon } from "@heroicons/react/24/solid";
import { useLicense } from "@/context/LicenseContext.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";
import ProductSearch from "@/components/ProductSearch.jsx";
import ThemeToggle from "./ThemeToggle.jsx";
import NotificationCenter from "./NotificationCenter.jsx";
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

export default function Topbar({ pageTitle, navigationStyle = "sidebar" }) {
  const [open, setOpen] = useState(false);
  const menuRef = useRef(null);
  const btnRef = useRef(null);

  // Notification center state
  const [notifOpen, setNotifOpen] = useState(false);
  const [notifCount, setNotifCount] = useState(0);

  const navigate = useNavigate();
  const { user, logout } = useAuth();
  const { theme } = useTheme();

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
  const buttonStyle = theme?.button_style || 'rounded';
  const isOutlined = buttonStyle === 'outlined';
  const buttonStyleClass = {
    'rounded': 'rounded-lg',
    'outlined': 'rounded-lg',
    'soft': 'rounded-xl',
  }[buttonStyle] || 'rounded-lg';

  // Get button background based on style (outlined = transparent)
  const getButtonBg = (color) => isOutlined ? 'transparent' : color;
  const getButtonBorder = (color) => isOutlined ? `2px solid ${color}` : 'none';
  const getButtonTextColor = (color) => isOutlined ? color : 'white';

  // Dynamic button class generator
  const getButtonClass = (baseColor, hoverColor, shadowColor) => 
    `inline-flex items-center gap-1.5 ${buttonStyleClass} px-3 py-1 text-xs font-semibold transition-all hover:scale-105 active:scale-95 focus:outline-none focus:ring-2 focus:ring-${baseColor}/50 relative z-50 ${isOutlined ? 'border-2' : 'shadow-lg'}`;

  const openInNewTab = (path) => window.open(path, "_blank", "noopener,noreferrer");

  useEffect(() => {
    const onKeyDown = (e) => {
      if (!e.altKey) return;
      const tag = (e.target?.tagName || "").toLowerCase();
      const isTyping =
        ["input", "textarea", "select"].includes(tag) || e.target?.isContentEditable;
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
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
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
    <div className="w-full z-40">
      <header
        role="banner"
        className={[
          "w-full bg-white/55 backdrop-blur-sm ring-1 ring-white/30 dark:bg-slate-800/70 dark:ring-white/10 shadow-[0_10px_30px_-12px_rgba(15,23,42,0.25)]",
          "relative px-4 py-3",
          "before:absolute before:inset-0 before:ring-1 before:ring-white/30 dark:before:ring-white/10",
        ].join(" ")}
      >
        <div className="flex flex-wrap items-center justify-between gap-3">
          {/* Left: Search */}
          <div className={`flex-1 min-w-[200px] ${navigationStyle === 'topbar' ? 'max-w-3xl' : 'max-w-[480px]'}`}>
            <ProductSearch navigationStyle={navigationStyle} />
          </div>

{/* Right: License badge + Quick actions + Theme Toggle + User */}
          <div className="flex items-center gap-2">
            {/* Theme Toggle */}
            <ThemeToggle />

            {/* Notification Bell */}
            <button
              onClick={() => setNotifOpen(true)}
              title="Open Notification Center"
              aria-label="Open notification center"
              className={`relative inline-flex items-center justify-center ${buttonStyleClass} p-2 transition-all hover:scale-105 active:scale-95 focus:outline-none focus:ring-2 focus:ring-${themeColors.primary}/50 ${isOutlined ? 'border-2' : 'shadow-lg'}`}
              style={{
                background: getButtonBg(themeColors.primary),
                border: getButtonBorder(themeColors.primary),
                color: getButtonTextColor(themeColors.primary),
                borderRadius: 'var(--btn-radius, 0.5rem)',
              }}
            >
              <BellIcon className="w-4 h-4" />
              {notifCount > 0 && (
                <span
                  className="absolute -top-1.5 -right-1.5 inline-flex items-center justify-center min-w-[18px] h-[18px] px-1 rounded-full text-[10px] font-bold text-white"
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
              onClick={() => navigate("/settings#license")}
              className={`inline-flex items-center gap-1.5 ${buttonStyleClass} px-3 py-1 text-xs font-semibold transition-all hover:scale-105 active:scale-95 focus:outline-none focus:ring-2 focus:ring-${themeColors.secondary}/50 relative z-50 ${isOutlined ? 'border-2' : 'shadow-lg'}`}
              style={{ 
                background: getButtonBg(themeColors.secondary),
                border: getButtonBorder(themeColors.secondary),
                color: getButtonTextColor(themeColors.secondary),
                borderRadius: 'var(--btn-radius, 0.5rem)',
              }}
              title={licValid ? "License is active" : "License required – click to activate"}
            >
              <ClockIcon className="w-3.5 h-3.5" />
              {licLoading ? "Checking…" : licValid ? `${leftTxt} left` : "Activate"}
            </button>

            <button
              onClick={() => openInNewTab("/purchase-invoices/create")}
              aria-keyshortcuts="Alt+1"
              title="Open Purchase Invoice (Alt+1) in a new tab"
              className={`inline-flex items-center gap-1.5 ${buttonStyleClass} px-3 py-1 text-xs font-semibold transition-all hover:scale-105 active:scale-95 focus:outline-none focus:ring-2 focus:ring-${themeColors.secondary}/50 relative z-50 ${isOutlined ? 'border-2' : 'shadow-lg'}`}
              style={{ 
                background: getButtonBg(themeColors.secondary),
                border: getButtonBorder(themeColors.secondary),
                color: getButtonTextColor(themeColors.secondary),
                borderRadius: 'var(--btn-radius, 0.5rem)',
              }}
            >
              <ClipboardDocumentListIcon className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Purchase</span>
            </button>

            <button
              onClick={() => openInNewTab("/sale-invoices/create/retail")}
              aria-keyshortcuts="Alt+2"
              title="Open Sale Invoice (Alt+2) in a new tab"
              className={`inline-flex items-center gap-1.5 ${buttonStyleClass} px-3 py-1 text-xs font-semibold transition-all hover:scale-105 active:scale-95 focus:outline-none focus:ring-2 focus:ring-${themeColors.primary}/50 relative z-50 ${isOutlined ? 'border-2' : 'shadow-lg'}`}
              style={{ 
                background: getButtonBg(themeColors.primary),
                border: getButtonBorder(themeColors.primary),
                color: getButtonTextColor(themeColors.primary),
                borderRadius: 'var(--btn-radius, 0.5rem)',
              }}
            >
              <ShoppingCartIcon className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Sale</span>
            </button>

            <div className="mx-1 h-6 w-px bg-gradient-to-b from-transparent via-slate-300/60 to-transparent" />

            {/* User menu */}
            <div className="relative">
              <button
                ref={btnRef}
                onClick={() => setOpen((v) => !v)}
                className={`flex items-center gap-1.5 ${buttonStyleClass} px-3 py-1 text-xs font-semibold transition-all hover:scale-105 active:scale-95 focus:outline-none focus:ring-2 focus:ring-${themeColors.primary}/50 relative z-50 ${isOutlined ? 'border-2' : 'shadow-lg'}`}
                style={{ 
                  background: getButtonBg(themeColors.primary),
                  border: getButtonBorder(themeColors.primary),
                  color: getButtonTextColor(themeColors.primary),
                  borderRadius: 'var(--btn-radius, 0.5rem)',
                }}
                aria-haspopup="menu"
                aria-expanded={open}
                aria-controls="topbar-user-menu"
              >
                <span 
                  className={`inline-grid h-5 w-5 place-items-center rounded-full bg-white/90 text-xs font-bold shadow ${isOutlined ? '' : ''}`}
                  style={{ color: themeColors.primary }}
                >
                  {(user?.name || "U").slice(0, 1).toUpperCase()}
                </span>
                <span className={`hidden sm:inline text-xs font-semibold ${isOutlined ? '' : 'text-white'}`} style={{ color: isOutlined ? themeColors.primary : undefined }}>
                  {user?.name || "User"}
                </span>
                <svg className={`w-3 h-3 ${isOutlined ? '' : 'text-white/90'}`} fill="none" stroke="currentColor" viewBox="0 0 24 24" style={{ color: isOutlined ? themeColors.primary : undefined }}>
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth="2" d="M19 9l-7 7-7-7" />
                </svg>
              </button>

              {open && (
                <div
                  id="topbar-user-menu"
                  ref={menuRef}
                  role="menu"
                  className={[
                    "absolute right-0 mt-2 w-48 rounded-xl overflow-hidden",
                    "bg-white/95 backdrop-blur ring-1 ring-gray-200/70 shadow-xl",
                    navigationStyle === 'topbar' ? 'z-[1000000]' : '',
                  ].join(" ")}
                >
                  <button
                    role="menuitem"
                    onClick={() => {
                      setOpen(false);
                      navigate("/profile");
                    }}
                    className="block w-full px-4 py-2 text-left text-sm hover:bg-white/80 focus:bg-white/80 focus:outline-none"
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
                    className="block w-full px-4 py-2 text-left text-sm hover:bg-white/80 focus:bg-white/80 focus:outline-none"
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
    </div>
  );
}

