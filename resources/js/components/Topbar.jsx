// src/components/Topbar.jsx
import React, { useEffect, useRef, useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext.jsx";
import { ClipboardDocumentListIcon, ShoppingCartIcon, ClockIcon } from "@heroicons/react/24/solid";
import { useLicense } from "@/context/LicenseContext.jsx";
import { useTheme } from "@/context/ThemeContext.jsx";
import ProductSearch from "@/components/ProductSearch.jsx";
import ThemeToggle from "./ThemeToggle.jsx";

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

  const navigate = useNavigate();
  const { user, logout } = useAuth();
  const { theme } = useTheme();

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

  // Dynamic button class generator
  const getButtonClass = (baseColor, hoverColor, shadowColor) => 
    `inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-white text-xs font-semibold shadow-lg transition-all hover:shadow-xl hover:scale-105 active:scale-95 focus:outline-none focus:ring-2 focus:ring-${baseColor}/50 relative z-50`;

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
        openInNewTab("/sale-invoices/create");
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

            {/* License badge */}
            <button
              onClick={() => navigate("/settings#license")}
              className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-white text-xs font-semibold transition-all hover:scale-105 active:scale-95 focus:outline-none relative z-50"
              style={{ 
                background: `linear-gradient(to right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                boxShadow: `0 4px 15px -3px ${themeColors.secondary}40`,
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
              className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-white text-xs font-semibold transition-all hover:scale-105 active:scale-95 focus:outline-none relative z-50"
              style={{ 
                background: `linear-gradient(to right, ${themeColors.secondary}, ${themeColors.secondaryHover})`,
                boxShadow: `0 4px 15px -3px ${themeColors.secondary}40`,
              }}
            >
              <ClipboardDocumentListIcon className="w-3.5 h-3.5" />
              <span className="hidden sm:inline">Purchase</span>
            </button>

            <button
              onClick={() => openInNewTab("/sale-invoices/create")}
              aria-keyshortcuts="Alt+2"
              title="Open Sale Invoice (Alt+2) in a new tab"
              className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-white text-xs font-semibold transition-all hover:scale-105 active:scale-95 focus:outline-none relative z-50"
              style={{ 
                background: `linear-gradient(to right, ${themeColors.primary}, ${themeColors.primaryHover})`,
                boxShadow: `0 4px 15px -3px ${themeColors.primary}40`,
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
                className="flex items-center gap-1.5 rounded-full px-3 py-1 transition-all hover:scale-105 active:scale-95 focus:outline-none"
                style={{ 
                  background: `linear-gradient(to right, ${themeColors.primary}, ${themeColors.secondaryHover})`,
                  boxShadow: `0 4px 15px -3px ${themeColors.primary}40`,
                }}
                aria-haspopup="menu"
                aria-expanded={open}
                aria-controls="topbar-user-menu"
              >
                <span 
                  className="inline-grid h-5 w-5 place-items-center rounded-full bg-white/90 text-xs font-bold shadow"
                  style={{ color: themeColors.primary }}
                >
                  {(user?.name || "U").slice(0, 1).toUpperCase()}
                </span>
                <span className="hidden sm:inline text-xs font-semibold text-white">
                  {user?.name || "User"}
                </span>
                <svg className="w-3 h-3 text-white/90" fill="none" stroke="currentColor" viewBox="0 0 24 24">
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
    </div>
  );
}

