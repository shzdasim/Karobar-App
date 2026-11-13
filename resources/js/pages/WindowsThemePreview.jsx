import React, { useState } from "react";
import { Link } from "react-router-dom";
import {
  SparklesIcon,
  SunIcon,
  MoonIcon,
  CurrencyDollarIcon,
  CubeIcon,
  ReceiptPercentIcon,
  PlusIcon,
  MagnifyingGlassIcon,
  AdjustmentsHorizontalIcon,
  ArrowUpRightIcon,
  TrashIcon,
  EyeIcon,
  DocumentDuplicateIcon,
  ArrowLeftIcon,
  ShieldCheckIcon,
  CheckIcon,
  CreditCardIcon,
  BanknotesIcon,
  BuildingStorefrontIcon,
  ArrowPathIcon,
  XMarkIcon,
  ChevronDownIcon,
  InformationCircleIcon,
  ExclamationTriangleIcon,
  CheckCircleIcon,
  XCircleIcon,
  Bars3Icon,
  FolderIcon,
  TagIcon,
  UserGroupIcon,
  ChartBarIcon,
  Cog6ToothIcon,
  ArrowDownTrayIcon,
  DocumentTextIcon,
  CalendarDaysIcon,
} from "@heroicons/react/24/outline";

export default function WindowsThemePreview() {
  const [isDark, setIsDark] = useState(false);
  const [accentKey, setAccentKey] = useState("windowsBlue");
  const [activeNav, setActiveNav] = useState("sales");
  const [activeTab, setActiveTab] = useState("all");
  const [showModal, setShowModal] = useState(false);
  const [toggleAutoReceipt, setToggleAutoReceipt] = useState(true);
  const [toggleEmailNotify, setToggleEmailNotify] = useState(false);
  const [toggleThermalPrint, setToggleThermalPrint] = useState(true);
  const [demoCheckbox1, setDemoCheckbox1] = useState(true);
  const [demoCheckbox2, setDemoCheckbox2] = useState(false);
  const [demoRadio, setDemoRadio] = useState("retail");
  const [demoPrice, setDemoPrice] = useState("24,500");
  const [demoQty, setDemoQty] = useState(4);
  const [demoCustomer, setDemoCustomer] = useState("Apex Global Logistics");
  const [demoItemName, setDemoItemName] = useState("Dell Latitude 7420 Ultrabook");
  const [demoTax, setDemoTax] = useState("18");
  const [demoNotes, setDemoNotes] = useState("Priority commercial order. Dispatch with warranty documentation.");
  const [sliderVal, setSliderVal] = useState(65);
  const [infoBarDismissed, setInfoBarDismissed] = useState(false);

  // Windows 11 Accent Presets
  const ACCENTS = {
    windowsBlue: {
      name: "Windows Blue",
      accent: isDark ? "#60CDFF" : "#005FB8",
      accentHover: isDark ? "#70D4FF" : "#0054A6",
      accentText: isDark ? "#000000" : "#FFFFFF",
      subtle: isDark ? "rgba(96, 205, 255, 0.12)" : "rgba(0, 95, 184, 0.08)",
      border: isDark ? "rgba(96, 205, 255, 0.4)" : "rgba(0, 95, 184, 0.3)",
    },
    mintTeal: {
      name: "Mint Teal",
      accent: isDark ? "#4CC2BA" : "#008272",
      accentHover: isDark ? "#5DD0C8" : "#007365",
      accentText: isDark ? "#000000" : "#FFFFFF",
      subtle: isDark ? "rgba(76, 194, 186, 0.12)" : "rgba(0, 130, 114, 0.08)",
      border: isDark ? "rgba(76, 194, 186, 0.4)" : "rgba(0, 130, 114, 0.3)",
    },
    emeraldGreen: {
      name: "Forest Green",
      accent: isDark ? "#6CCB5F" : "#0F7B0F",
      accentHover: isDark ? "#7BD76E" : "#0B6A0B",
      accentText: isDark ? "#000000" : "#FFFFFF",
      subtle: isDark ? "rgba(108, 203, 95, 0.12)" : "rgba(15, 123, 15, 0.08)",
      border: isDark ? "rgba(108, 203, 95, 0.4)" : "rgba(15, 123, 15, 0.3)",
    },
    sunsetOrange: {
      name: "Sunset Orange",
      accent: isDark ? "#FF8C68" : "#C43E1C",
      accentHover: isDark ? "#FF9A7B" : "#AF3618",
      accentText: isDark ? "#000000" : "#FFFFFF",
      subtle: isDark ? "rgba(255, 140, 104, 0.12)" : "rgba(196, 62, 28, 0.08)",
      border: isDark ? "rgba(255, 140, 104, 0.4)" : "rgba(196, 62, 28, 0.3)",
    },
    plumViolet: {
      name: "Plum Violet",
      accent: isDark ? "#B180F0" : "#744DA9",
      accentHover: isDark ? "#BE94F3" : "#674397",
      accentText: isDark ? "#000000" : "#FFFFFF",
      subtle: isDark ? "rgba(177, 128, 240, 0.12)" : "rgba(116, 77, 169, 0.08)",
      border: isDark ? "rgba(177, 128, 240, 0.4)" : "rgba(116, 77, 169, 0.3)",
    },
  };

  const currentAccent = ACCENTS[accentKey] || ACCENTS.windowsBlue;

  const sampleLedger = [
    {
      id: "INV-8001",
      customer: "Beacon Health Systems",
      date: "13 Nov 2026",
      amount: "PKR 342,000",
      status: "Completed",
      payment: "Bank Transfer",
    },
    {
      id: "INV-8002",
      customer: "Indus Wholesale Co.",
      date: "12 Nov 2026",
      amount: "PKR 118,500",
      status: "Processing",
      payment: "Cheque",
    },
    {
      id: "INV-8003",
      customer: "Vertex Horizon Retail",
      date: "11 Nov 2026",
      amount: "PKR 89,200",
      status: "Completed",
      payment: "Cash",
    },
    {
      id: "INV-8004",
      customer: "Crescent Distribution Ltd",
      date: "10 Nov 2026",
      amount: "PKR 540,000",
      status: "On Hold",
      payment: "Credit Term",
    },
  ];

  return (
    <div
      className={`min-h-screen text-sm transition-colors duration-200 ${
        isDark
          ? "bg-[#202020] text-[#E0E0E0]" // Windows 11 Dark Mica Canvas
          : "bg-[#F3F3F3] text-[#1B1B1B]" // Windows 11 Light Mica Canvas
      }`}
      style={{
        fontFamily:
          '"Segoe UI Variable Text", "Segoe UI", -apple-system, BlinkMacSystemFont, Roboto, "Helvetica Neue", sans-serif',
      }}
    >
      {/* =========================================================================
          WINDOWS 11 MICA WINDOW CHASSIS (AUTHENTIC DESKTOP APP FRAME)
          ========================================================================= */}
      <div className="max-w-[1440px] mx-auto p-2 sm:p-4 md:p-6 space-y-4">
        {/* TOP WINDOW TITLE BAR & COMMAND HEADER */}
        <div
          className={`rounded-t-lg border border-b-0 px-4 py-2 flex items-center justify-between transition-colors select-none ${
            isDark
              ? "bg-[#1C1C1C] border-[#333333] text-[#CCCCCC]"
              : "bg-[#EAEAEA] border-[#D1D1D1] text-[#444444]"
          }`}
        >
          {/* App Title & Icon */}
          <div className="flex items-center gap-3">
            {/* Windows 11 Fluent Icon */}
            <div className="w-5 h-5 flex items-center justify-center">
              <svg className="w-4 h-4" viewBox="0 0 16 16" fill="none">
                <rect x="1" y="1" width="6.5" height="6.5" rx="0.5" fill={currentAccent.accent} />
                <rect x="8.5" y="1" width="6.5" height="6.5" rx="0.5" fill={currentAccent.accent} />
                <rect x="1" y="8.5" width="6.5" height="6.5" rx="0.5" fill={currentAccent.accent} />
                <rect x="8.5" y="8.5" width="6.5" height="6.5" rx="0.5" fill={currentAccent.accent} />
              </svg>
            </div>
            <span className="text-xs font-semibold tracking-wide">
              Karobar ERP — Windows 11 Fluent Design System
            </span>
            <span
              className={`text-[10px] font-mono uppercase px-1.5 py-0.2 rounded border ${
                isDark
                  ? "bg-[#2A2A2A] border-[#3D3D3D] text-[#888888]"
                  : "bg-[#FFFFFF] border-[#E0E0E0] text-[#666666]"
              }`}
            >
              WinUI 3
            </span>
          </div>

          {/* Quick Search in Title Bar */}
          <div className="hidden md:flex items-center w-80 relative">
            <MagnifyingGlassIcon
              className={`w-3.5 h-3.5 absolute left-2.5 pointer-events-none ${
                isDark ? "text-neutral-400" : "text-neutral-500"
              }`}
            />
            <input
              type="text"
              readOnly
              value="Search transactions, customers or vouchers (Ctrl+E)"
              className={`w-full h-7 pl-8 pr-3 text-[11px] rounded border transition-colors outline-none cursor-pointer ${
                isDark
                  ? "bg-[#252525] border-[#383838] text-[#888888]"
                  : "bg-[#FFFFFF] border-[#D6D6D6] text-[#777777]"
              }`}
            />
          </div>

          {/* Classic Windows 11 Window Controls */}
          <div className="flex items-center gap-1">
            <button
              title="Minimize"
              className={`w-9 h-7 flex items-center justify-center rounded transition-colors ${
                isDark ? "hover:bg-[#2C2C2C] text-[#AAAAAA]" : "hover:bg-[#DCDCDC] text-[#555555]"
              }`}
            >
              <span className="text-sm font-bold leading-none">─</span>
            </button>
            <button
              title="Maximize"
              className={`w-9 h-7 flex items-center justify-center rounded transition-colors ${
                isDark ? "hover:bg-[#2C2C2C] text-[#AAAAAA]" : "hover:bg-[#DCDCDC] text-[#555555]"
              }`}
            >
              <div className="w-2.5 h-2.5 border border-current rounded-xs" />
            </button>
            <button
              title="Close"
              className="w-9 h-7 flex items-center justify-center rounded transition-colors hover:bg-[#C42B1C] hover:text-white text-current"
            >
              <XMarkIcon className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>

        {/* =========================================================================
            COMMAND BAR (WINDOWS 11 COMMANDBAR / RIBBON)
            ========================================================================= */}
        <div
          className={`border border-t-0 p-3 sm:p-4 flex flex-wrap items-center justify-between gap-3 transition-colors ${
            isDark
              ? "bg-[#242424] border-[#333333]"
              : "bg-[#F9F9F9] border-[#D1D1D1]"
          }`}
        >
          {/* Left Actions */}
          <div className="flex flex-wrap items-center gap-2">
            <Link
              to="/settings"
              className={`h-8 px-3 rounded flex items-center gap-1.5 text-xs font-medium border transition-colors ${
                isDark
                  ? "bg-[#2D2D2D] hover:bg-[#353535] border-[#3D3D3D] text-[#E0E0E0]"
                  : "bg-[#FFFFFF] hover:bg-[#F5F5F5] border-[#D9D9D9] text-[#222222] shadow-[0_1px_2px_rgba(0,0,0,0.04)]"
              }`}
            >
              <ArrowLeftIcon className="w-3.5 h-3.5" />
              <span>Back to Settings</span>
            </Link>

            <button
              onClick={() => setShowModal(true)}
              className="h-8 px-3.5 rounded flex items-center gap-1.5 text-xs font-semibold text-white transition-all active:scale-[0.98]"
              style={{
                backgroundColor: currentAccent.accent,
                color: currentAccent.accentText,
                borderBottom: `2px solid rgba(0,0,0,0.22)`,
              }}
            >
              <PlusIcon className="w-3.5 h-3.5 stroke-[2.5]" />
              <span>New Transaction (ContentDialog)</span>
            </button>

            <button
              className={`h-8 px-3 rounded flex items-center gap-1.5 text-xs font-medium border transition-colors ${
                isDark
                  ? "bg-[#2D2D2D] hover:bg-[#353535] border-[#3D3D3D] text-[#E0E0E0]"
                  : "bg-[#FFFFFF] hover:bg-[#F5F5F5] border-[#D9D9D9] text-[#222222] shadow-[0_1px_2px_rgba(0,0,0,0.04)]"
              }`}
            >
              <ArrowDownTrayIcon className="w-3.5 h-3.5" />
              <span>Export Excel</span>
            </button>

            <button
              className={`h-8 px-3 rounded flex items-center gap-1.5 text-xs font-medium border transition-colors ${
                isDark
                  ? "bg-[#2D2D2D] hover:bg-[#353535] border-[#3D3D3D] text-[#E0E0E0]"
                  : "bg-[#FFFFFF] hover:bg-[#F5F5F5] border-[#D9D9D9] text-[#222222] shadow-[0_1px_2px_rgba(0,0,0,0.04)]"
              }`}
            >
              <ArrowPathIcon className="w-3.5 h-3.5" />
              <span>Refresh</span>
            </button>
          </div>

          {/* Right: Theme Switcher & Windows Accent Picker */}
          <div className="flex items-center gap-3">
            {/* Color Accent Picker */}
            <div className="flex items-center gap-1.5">
              <span className="text-[11px] opacity-70 hidden sm:inline">Accent:</span>
              <div className="flex items-center gap-1">
                {Object.entries(ACCENTS).map(([key, item]) => {
                  const isSelected = accentKey === key;
                  return (
                    <button
                      key={key}
                      onClick={() => setAccentKey(key)}
                      title={item.name}
                      className={`w-5 h-5 rounded-full transition-transform ${
                        isSelected ? "ring-2 ring-offset-2 scale-110" : "hover:scale-105 opacity-80"
                      }`}
                      style={{
                        backgroundColor: item.accent,
                        ringColor: item.accent,
                        ringOffsetColor: isDark ? "#242424" : "#F9F9F9",
                      }}
                    />
                  );
                })}
              </div>
            </div>

            {/* Dark/Light Mode Button */}
            <button
              onClick={() => setIsDark(!isDark)}
              className={`h-8 px-3 rounded flex items-center gap-1.5 text-xs font-medium border transition-colors ${
                isDark
                  ? "bg-[#2D2D2D] hover:bg-[#353535] border-[#3D3D3D] text-[#E0E0E0]"
                  : "bg-[#FFFFFF] hover:bg-[#F5F5F5] border-[#D9D9D9] text-[#222222] shadow-[0_1px_2px_rgba(0,0,0,0.04)]"
              }`}
            >
              {isDark ? (
                <>
                  <SunIcon className="w-3.5 h-3.5 text-amber-400" />
                  <span>Light Mode</span>
                </>
              ) : (
                <>
                  <MoonIcon className="w-3.5 h-3.5 text-slate-700" />
                  <span>Dark Mode</span>
                </>
              )}
            </button>
          </div>
        </div>

        {/* =========================================================================
            WINDOWS 11 INFOBAR (SYSTEM STATUS BANNER)
            ========================================================================= */}
        {!infoBarDismissed && (
          <div
            className={`p-3 rounded-lg border flex items-start justify-between gap-3 transition-colors ${
              isDark
                ? "bg-[#25282D] border-[#303B47] text-[#DCE6F2]"
                : "bg-[#EFF6FC] border-[#C7E0F4] text-[#103E65]"
            }`}
          >
            <div className="flex items-center gap-2.5">
              <InformationCircleIcon
                className="w-5 h-5 shrink-0"
                style={{ color: currentAccent.accent }}
              />
              <div className="text-xs leading-relaxed">
                <strong>Windows 11 Fluent Theme Preview:</strong> All forms, inputs, buttons,
                toggles, and data grids below follow Microsoft's official WinUI 3 guidelines (4px/8px
                radius, mica elevation, bottom focus lines, and Segoe typography).
              </div>
            </div>
            <button
              onClick={() => setInfoBarDismissed(true)}
              className="p-1 rounded hover:bg-black/10 dark:hover:bg-white/10 text-current transition"
            >
              <XMarkIcon className="w-4 h-4" />
            </button>
          </div>
        )}

        {/* =========================================================================
            MAIN APP WORKSPACE: SIDEBAR + CONTENT AREA
            ========================================================================= */}
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-4">
          {/* NAVIGATION VIEW (WINDOWS 11 SIDEBAR WITH LEFT ACTIVE PILL) */}
          <div
            className={`lg:col-span-3 rounded-lg border p-2 space-y-1 transition-colors ${
              isDark
                ? "bg-[#262626] border-[#363636]"
                : "bg-[#FFFFFF] border-[#E3E3E3] shadow-[0_2px_4px_rgba(0,0,0,0.02)]"
            }`}
          >
            <div className="px-3 py-2 flex items-center justify-between text-xs font-semibold opacity-60">
              <span>NAVIGATION VIEW</span>
              <Bars3Icon className="w-4 h-4 cursor-pointer" />
            </div>

            {[
              { id: "dashboard", label: "Dashboard", icon: ChartBarIcon, count: null },
              { id: "sales", label: "Sales & Invoicing", icon: ReceiptPercentIcon, count: "14" },
              { id: "products", label: "Inventory & Items", icon: CubeIcon, count: null },
              { id: "customers", label: "Customer Accounts", icon: UserGroupIcon, count: "380" },
              { id: "finance", label: "General Ledger", icon: BanknotesIcon, count: null },
              { id: "settings", label: "Settings", icon: Cog6ToothIcon, count: null },
            ].map((item) => {
              const isActive = activeNav === item.id;
              const IconComp = item.icon;
              return (
                <button
                  key={item.id}
                  onClick={() => setActiveNav(item.id)}
                  className={`w-full h-9 px-3 rounded flex items-center justify-between text-xs font-medium transition-all relative ${
                    isActive
                      ? isDark
                        ? "bg-[#333333] text-white font-semibold"
                        : "bg-[#EAEAEA] text-[#000000] font-semibold"
                      : isDark
                      ? "hover:bg-[#2C2C2C] text-[#BBBBBB]"
                      : "hover:bg-[#F2F2F2] text-[#444444]"
                  }`}
                >
                  {/* Signature Windows 11 Active Left Pill Bar */}
                  {isActive && (
                    <span
                      className="absolute left-0 top-2 bottom-2 w-1 rounded-full transition-all"
                      style={{ backgroundColor: currentAccent.accent }}
                    />
                  )}

                  <div className="flex items-center gap-2.5 pl-1.5">
                    <IconComp
                      className="w-4 h-4 shrink-0"
                      style={{ color: isActive ? currentAccent.accent : undefined }}
                    />
                    <span>{item.label}</span>
                  </div>

                  {item.count && (
                    <span
                      className={`text-[10px] font-semibold px-1.5 py-0.2 rounded-full ${
                        isActive
                          ? "bg-black/10 dark:bg-white/10"
                          : "bg-neutral-200/60 dark:bg-neutral-700/60 opacity-80"
                      }`}
                    >
                      {item.count}
                    </span>
                  )}
                </button>
              );
            })}

            <div className="pt-4 pb-2 px-3 border-t border-current/10 text-[11px] opacity-60 font-semibold">
              PREFERENCE FLYOUTS
            </div>

            <div className="p-3 rounded border space-y-3 text-xs bg-current/3 border-current/10">
              <div className="flex items-center justify-between">
                <span>Thermal Auto-Print</span>
                {/* Windows 11 ToggleSwitch */}
                <button
                  onClick={() => setToggleThermalPrint(!toggleThermalPrint)}
                  className={`w-10 h-5 rounded-full relative transition-colors focus:outline-none ${
                    toggleThermalPrint ? "" : isDark ? "bg-[#3A3A3A]" : "bg-[#CCCCCC]"
                  }`}
                  style={{
                    backgroundColor: toggleThermalPrint ? currentAccent.accent : undefined,
                  }}
                >
                  <span
                    className={`block w-3.5 h-3.5 rounded-full bg-white transition-transform ${
                      toggleThermalPrint ? "translate-x-5" : "translate-x-1"
                    }`}
                  />
                </button>
              </div>

              <div className="flex items-center justify-between">
                <span>Email Invoices</span>
                <button
                  onClick={() => setToggleEmailNotify(!toggleEmailNotify)}
                  className={`w-10 h-5 rounded-full relative transition-colors focus:outline-none ${
                    toggleEmailNotify ? "" : isDark ? "bg-[#3A3A3A]" : "bg-[#CCCCCC]"
                  }`}
                  style={{
                    backgroundColor: toggleEmailNotify ? currentAccent.accent : undefined,
                  }}
                >
                  <span
                    className={`block w-3.5 h-3.5 rounded-full bg-white transition-transform ${
                      toggleEmailNotify ? "translate-x-5" : "translate-x-1"
                    }`}
                  />
                </button>
              </div>
            </div>
          </div>

          {/* MAIN FORM & COMPONENT PLAYGROUND */}
          <div className="lg:col-span-9 space-y-4">
            {/* =====================================================================
                SECTION 1: FORMS & INPUT FIELDS (WINUI 3 CONTROLS)
                ===================================================================== */}
            <div
              className={`rounded-lg border p-5 sm:p-6 transition-colors ${
                isDark
                  ? "bg-[#262626] border-[#363636]"
                  : "bg-[#FFFFFF] border-[#E3E3E3] shadow-[0_2px_4px_rgba(0,0,0,0.02)]"
              }`}
            >
              <div className="flex items-center justify-between pb-3 mb-4 border-b border-current/10">
                <div>
                  <h2 className="text-base font-bold">1. Windows 11 Form Fields & Input Controls</h2>
                  <p className="text-xs opacity-70">
                    Featuring authentic 2px bottom focus indicators, micro-steppers, and Fluent Select
                    flyouts.
                  </p>
                </div>
                <span
                  className="px-2 py-0.5 rounded text-[11px] font-semibold"
                  style={{
                    backgroundColor: currentAccent.subtle,
                    color: currentAccent.accent,
                  }}
                >
                  WinUI 3 TextBoxes
                </span>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
                {/* Field 1: Standard Textbox */}
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold block">Customer Name / Title</label>
                  <div className="relative">
                    <input
                      type="text"
                      value={demoCustomer}
                      onChange={(e) => setDemoCustomer(e.target.value)}
                      className={`w-full h-9 px-3 text-xs rounded transition-all outline-none ${
                        isDark
                          ? "bg-[#2D2D2D] text-white border border-[#404040] focus:border-b-2"
                          : "bg-[#FFFFFF] text-black border border-[#D1D1D1] focus:border-b-2"
                      }`}
                      style={{
                        borderBottomColor: currentAccent.accent,
                      }}
                    />
                  </div>
                  <span className="text-[11px] opacity-60">Standard single-line text input</span>
                </div>

                {/* Field 2: Item Description */}
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold block">Product Description</label>
                  <input
                    type="text"
                    value={demoItemName}
                    onChange={(e) => setDemoItemName(e.target.value)}
                    className={`w-full h-9 px-3 text-xs rounded transition-all outline-none ${
                      isDark
                        ? "bg-[#2D2D2D] text-white border border-[#404040] focus:border-b-2"
                        : "bg-[#FFFFFF] text-black border border-[#D1D1D1] focus:border-b-2"
                    }`}
                    style={{
                      borderBottomColor: currentAccent.accent,
                    }}
                  />
                  <span className="text-[11px] opacity-60">Focus to see bottom accent line</span>
                </div>

                {/* Field 3: Windows NumberBox with Right Micro-Stepper */}
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold block">Quantity (NumberBox)</label>
                  <div
                    className={`flex items-center h-9 rounded border transition-all overflow-hidden ${
                      isDark ? "bg-[#2D2D2D] border-[#404040]" : "bg-[#FFFFFF] border-[#D1D1D1]"
                    }`}
                  >
                    <input
                      type="number"
                      value={demoQty}
                      onChange={(e) => setDemoQty(parseInt(e.target.value) || 0)}
                      className="w-full h-full px-3 text-xs bg-transparent outline-none font-semibold"
                    />
                    <div className="flex flex-col h-full border-l border-current/15 shrink-0 w-7">
                      <button
                        type="button"
                        onClick={() => setDemoQty(demoQty + 1)}
                        className="h-1/2 flex items-center justify-center hover:bg-current/10 text-[9px] font-bold"
                      >
                        ▲
                      </button>
                      <button
                        type="button"
                        onClick={() => setDemoQty(Math.max(1, demoQty - 1))}
                        className="h-1/2 flex items-center justify-center hover:bg-current/10 text-[9px] font-bold border-t border-current/15"
                      >
                        ▼
                      </button>
                    </div>
                  </div>
                  <span className="text-[11px] opacity-60">Integrated spinner steppers</span>
                </div>

                {/* Field 4: Price with Currency Unit */}
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold block">Unit Price</label>
                  <div
                    className={`flex items-center h-9 rounded border transition-all ${
                      isDark ? "bg-[#2D2D2D] border-[#404040]" : "bg-[#FFFFFF] border-[#D1D1D1]"
                    }`}
                  >
                    <span className="px-2.5 text-[11px] font-bold opacity-60">PKR</span>
                    <input
                      type="text"
                      value={demoPrice}
                      onChange={(e) => setDemoPrice(e.target.value)}
                      className="w-full h-full pr-3 text-xs bg-transparent outline-none font-bold"
                    />
                  </div>
                  <span className="text-[11px] opacity-60">Prefixed currency container</span>
                </div>

                {/* Field 5: ComboBox (Dropdown) */}
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold block">Tax Schedule (ComboBox)</label>
                  <div className="relative">
                    <select
                      value={demoTax}
                      onChange={(e) => setDemoTax(e.target.value)}
                      className={`w-full h-9 pl-3 pr-8 text-xs rounded appearance-none outline-none cursor-pointer transition-all ${
                        isDark
                          ? "bg-[#2D2D2D] text-white border border-[#404040]"
                          : "bg-[#FFFFFF] text-black border-[#D1D1D1] border"
                      }`}
                    >
                      <option value="18">Standard 18% Commercial Sales Tax</option>
                      <option value="0">Zero-Rated Healthcare Exemption</option>
                      <option value="5">Reduced Tier 5% Wholesale Exemption</option>
                    </select>
                    <ChevronDownIcon className="w-3.5 h-3.5 absolute right-2.5 top-3 pointer-events-none opacity-60" />
                  </div>
                  <span className="text-[11px] opacity-60">Fluent dropdown with chevron</span>
                </div>

                {/* Field 6: DatePicker Box */}
                <div className="space-y-1.5">
                  <label className="text-xs font-semibold block">Invoice Date (DatePicker)</label>
                  <div
                    className={`flex items-center justify-between h-9 px-3 rounded border transition-all cursor-pointer ${
                      isDark ? "bg-[#2D2D2D] border-[#404040]" : "bg-[#FFFFFF] border-[#D1D1D1]"
                    }`}
                  >
                    <span className="text-xs font-medium">13 November 2026</span>
                    <CalendarDaysIcon className="w-4 h-4 opacity-60" />
                  </div>
                  <span className="text-[11px] opacity-60">Windows DatePicker glyph</span>
                </div>

                {/* Field 7: Multiline Notes */}
                <div className="md:col-span-3 space-y-1.5">
                  <label className="text-xs font-semibold block">Disbursement & Delivery Notes</label>
                  <textarea
                    rows={2}
                    value={demoNotes}
                    onChange={(e) => setDemoNotes(e.target.value)}
                    className={`w-full p-3 text-xs rounded transition-all outline-none resize-none ${
                      isDark
                        ? "bg-[#2D2D2D] text-white border border-[#404040] focus:border-b-2"
                        : "bg-[#FFFFFF] text-black border border-[#D1D1D1] focus:border-b-2"
                    }`}
                    style={{
                      borderBottomColor: currentAccent.accent,
                    }}
                  />
                </div>
              </div>

              {/* Toggles, Checkboxes & Radio Matrix */}
              <div className="mt-6 pt-5 border-t border-current/10 grid grid-cols-1 sm:grid-cols-3 gap-6">
                {/* Windows 11 Toggle Switches */}
                <div className="space-y-3">
                  <span className="text-xs font-bold block uppercase tracking-wider opacity-60">
                    ToggleSwitches
                  </span>
                  <div className="flex items-center gap-3">
                    <button
                      onClick={() => setToggleAutoReceipt(!toggleAutoReceipt)}
                      className={`w-10 h-5 rounded-full relative transition-colors focus:outline-none ${
                        toggleAutoReceipt ? "" : isDark ? "bg-[#3A3A3A]" : "bg-[#CCCCCC]"
                      }`}
                      style={{
                        backgroundColor: toggleAutoReceipt ? currentAccent.accent : undefined,
                      }}
                    >
                      <span
                        className={`block w-3.5 h-3.5 rounded-full bg-white transition-transform ${
                          toggleAutoReceipt ? "translate-x-5" : "translate-x-1"
                        }`}
                      />
                    </button>
                    <span className="text-xs font-medium">Auto-generate e-receipt</span>
                  </div>

                  <div className="flex items-center gap-3">
                    <button
                      onClick={() => setToggleEmailNotify(!toggleEmailNotify)}
                      className={`w-10 h-5 rounded-full relative transition-colors focus:outline-none ${
                        toggleEmailNotify ? "" : isDark ? "bg-[#3A3A3A]" : "bg-[#CCCCCC]"
                      }`}
                      style={{
                        backgroundColor: toggleEmailNotify ? currentAccent.accent : undefined,
                      }}
                    >
                      <span
                        className={`block w-3.5 h-3.5 rounded-full bg-white transition-transform ${
                          toggleEmailNotify ? "translate-x-5" : "translate-x-1"
                        }`}
                      />
                    </button>
                    <span className="text-xs font-medium">Send SMS confirmation</span>
                  </div>
                </div>

                {/* Windows 11 Square Checkboxes */}
                <div className="space-y-3">
                  <span className="text-xs font-bold block uppercase tracking-wider opacity-60">
                    Checkboxes
                  </span>
                  <label className="flex items-center gap-2.5 cursor-pointer text-xs font-medium">
                    <button
                      type="button"
                      onClick={() => setDemoCheckbox1(!demoCheckbox1)}
                      className={`w-4 h-4 rounded-xs border flex items-center justify-center transition-colors ${
                        demoCheckbox1 ? "text-white" : isDark ? "border-[#555555]" : "border-[#888888]"
                      }`}
                      style={{
                        backgroundColor: demoCheckbox1 ? currentAccent.accent : "transparent",
                        borderColor: demoCheckbox1 ? currentAccent.accent : undefined,
                      }}
                    >
                      {demoCheckbox1 && <CheckIcon className="w-3 h-3 stroke-[3]" />}
                    </button>
                    <span>Apply FBR tax exemption barcode</span>
                  </label>

                  <label className="flex items-center gap-2.5 cursor-pointer text-xs font-medium">
                    <button
                      type="button"
                      onClick={() => setDemoCheckbox2(!demoCheckbox2)}
                      className={`w-4 h-4 rounded-xs border flex items-center justify-center transition-colors ${
                        demoCheckbox2 ? "text-white" : isDark ? "border-[#555555]" : "border-[#888888]"
                      }`}
                      style={{
                        backgroundColor: demoCheckbox2 ? currentAccent.accent : "transparent",
                        borderColor: demoCheckbox2 ? currentAccent.accent : undefined,
                      }}
                    >
                      {demoCheckbox2 && <CheckIcon className="w-3 h-3 stroke-[3]" />}
                    </button>
                    <span>Include batch tracking number</span>
                  </label>
                </div>

                {/* Windows 11 Radio Buttons */}
                <div className="space-y-3">
                  <span className="text-xs font-bold block uppercase tracking-wider opacity-60">
                    Radio Buttons
                  </span>
                  <div className="flex items-center gap-4">
                    <label className="flex items-center gap-2 cursor-pointer text-xs font-medium">
                      <input
                        type="radio"
                        name="channel"
                        checked={demoRadio === "retail"}
                        onChange={() => setDemoRadio("retail")}
                        className="accent-current w-3.5 h-3.5"
                        style={{ accentColor: currentAccent.accent }}
                      />
                      <span>Retail POS</span>
                    </label>

                    <label className="flex items-center gap-2 cursor-pointer text-xs font-medium">
                      <input
                        type="radio"
                        name="channel"
                        checked={demoRadio === "wholesale"}
                        onChange={() => setDemoRadio("wholesale")}
                        className="accent-current w-3.5 h-3.5"
                        style={{ accentColor: currentAccent.accent }}
                      />
                      <span>Wholesale B2B</span>
                    </label>
                  </div>
                </div>
              </div>
            </div>

            {/* =====================================================================
                SECTION 2: BUTTONS SYSTEM (WINDOWS 11 FLUENT SPEC)
                ===================================================================== */}
            <div
              className={`rounded-lg border p-5 sm:p-6 transition-colors ${
                isDark
                  ? "bg-[#262626] border-[#363636]"
                  : "bg-[#FFFFFF] border-[#E3E3E3] shadow-[0_2px_4px_rgba(0,0,0,0.02)]"
              }`}
            >
              <div className="flex items-center justify-between pb-3 mb-4 border-b border-current/10">
                <div>
                  <h2 className="text-base font-bold">2. Windows 11 Button Hierarchy</h2>
                  <p className="text-xs opacity-70">
                    Accent button (resting bottom elevation), Standard button, Subtle button, SplitButton.
                  </p>
                </div>
                <span className="text-xs font-mono opacity-60">4px Corner Radius</span>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                {/* Accent Button */}
                <div className="space-y-1.5">
                  <span className="text-xs font-semibold opacity-60 block">Accent (Primary)</span>
                  <button
                    className="w-full h-9 px-4 rounded text-xs font-semibold flex items-center justify-center gap-2 transition-all active:scale-[0.98]"
                    style={{
                      backgroundColor: currentAccent.accent,
                      color: currentAccent.accentText,
                      borderBottom: "2px solid rgba(0,0,0,0.25)",
                    }}
                  >
                    <PlusIcon className="w-3.5 h-3.5 stroke-[2.5]" />
                    <span>Create Invoice</span>
                  </button>
                  <span className="text-[10px] opacity-60 block">3D bottom border bevel</span>
                </div>

                {/* Standard Neutral Button */}
                <div className="space-y-1.5">
                  <span className="text-xs font-semibold opacity-60 block">Standard (Secondary)</span>
                  <button
                    className={`w-full h-9 px-4 rounded text-xs font-medium flex items-center justify-center gap-2 border transition-all active:scale-[0.98] ${
                      isDark
                        ? "bg-[#2D2D2D] hover:bg-[#353535] border-[#3D3D3D] text-[#E0E0E0]"
                        : "bg-[#FFFFFF] hover:bg-[#F7F7F7] border-[#D9D9D9] text-[#222222] shadow-[0_1px_2px_rgba(0,0,0,0.04)]"
                    }`}
                    style={{
                      borderBottom: isDark ? "2px solid #222222" : "2px solid rgba(0,0,0,0.14)",
                    }}
                  >
                    <DocumentDuplicateIcon className="w-3.5 h-3.5" />
                    <span>Duplicate Entry</span>
                  </button>
                  <span className="text-[10px] opacity-60 block">Neutral elevation plate</span>
                </div>

                {/* Subtle / Ghost Button */}
                <div className="space-y-1.5">
                  <span className="text-xs font-semibold opacity-60 block">Subtle (Ghost Action)</span>
                  <button
                    className="w-full h-9 px-4 rounded text-xs font-medium flex items-center justify-center gap-2 hover:bg-current/8 transition-colors text-current"
                  >
                    <EyeIcon className="w-3.5 h-3.5" />
                    <span>Print Preview</span>
                  </button>
                  <span className="text-[10px] opacity-60 block">Zero border, soft hover</span>
                </div>

                {/* Destructive Button */}
                <div className="space-y-1.5">
                  <span className="text-xs font-semibold opacity-60 block">Destructive Action</span>
                  <button
                    className="w-full h-9 px-4 rounded text-xs font-semibold flex items-center justify-center gap-2 bg-[#C42B1C] hover:bg-[#B32719] text-white transition-all active:scale-[0.98]"
                    style={{
                      borderBottom: "2px solid rgba(0,0,0,0.3)",
                    }}
                  >
                    <TrashIcon className="w-3.5 h-3.5" />
                    <span>Void Bill</span>
                  </button>
                  <span className="text-[10px] opacity-60 block">Windows 11 Crimson #C42B1C</span>
                </div>
              </div>

              {/* Windows 11 SplitButton Demo */}
              <div className="mt-5 pt-4 border-t border-current/10 flex flex-wrap items-center gap-4">
                <div className="flex items-center">
                  <button
                    className="h-8 px-3 rounded-l text-xs font-semibold flex items-center gap-1.5"
                    style={{
                      backgroundColor: currentAccent.accent,
                      color: currentAccent.accentText,
                      borderBottom: "2px solid rgba(0,0,0,0.22)",
                    }}
                  >
                    <span>Save & Print Thermal</span>
                  </button>
                  <button
                    className="h-8 px-2 rounded-r border-l border-black/20 flex items-center justify-center"
                    style={{
                      backgroundColor: currentAccent.accent,
                      color: currentAccent.accentText,
                      borderBottom: "2px solid rgba(0,0,0,0.22)",
                    }}
                  >
                    <ChevronDownIcon className="w-3.5 h-3.5 stroke-[2.5]" />
                  </button>
                </div>

                <div className="flex items-center gap-2">
                  <span className="text-xs opacity-70">Slider Value ({sliderVal}%):</span>
                  <input
                    type="range"
                    min="10"
                    max="100"
                    value={sliderVal}
                    onChange={(e) => setSliderVal(parseInt(e.target.value))}
                    className="w-36 h-1 rounded cursor-pointer"
                    style={{ accentColor: currentAccent.accent }}
                  />
                </div>
              </div>
            </div>

            {/* =====================================================================
                SECTION 3: DATAGRID & TABVIEW (WINDOWS 11 TABULAR INTERFACE)
                ===================================================================== */}
            <div
              className={`rounded-lg border overflow-hidden transition-colors ${
                isDark
                  ? "bg-[#262626] border-[#363636]"
                  : "bg-[#FFFFFF] border-[#E3E3E3] shadow-[0_2px_4px_rgba(0,0,0,0.02)]"
              }`}
            >
              {/* TabView Bar */}
              <div
                className={`px-4 pt-2 border-b flex items-center gap-6 text-xs font-medium transition-colors ${
                  isDark ? "bg-[#202020] border-[#333333]" : "bg-[#F3F3F3] border-[#E5E5E5]"
                }`}
              >
                {[
                  { id: "all", label: "All Transactions" },
                  { id: "unpaid", label: "Awaiting Clearance" },
                  { id: "completed", label: "Settled Ledger" },
                ].map((tab) => {
                  const isActive = activeTab === tab.id;
                  return (
                    <button
                      key={tab.id}
                      onClick={() => setActiveTab(tab.id)}
                      className={`pb-2.5 pt-1 relative transition-colors font-medium ${
                        isActive
                          ? "text-current font-bold"
                          : "opacity-60 hover:opacity-100"
                      }`}
                    >
                      <span>{tab.label}</span>
                      {isActive && (
                        <span
                          className="absolute bottom-0 left-0 right-0 h-0.5 rounded-full"
                          style={{ backgroundColor: currentAccent.accent }}
                        />
                      )}
                    </button>
                  );
                })}
              </div>

              {/* DataGrid Table */}
              <div className="overflow-x-auto">
                <table className="w-full text-left text-xs border-collapse">
                  <thead>
                    <tr
                      className={`border-b text-[11px] font-semibold opacity-70 ${
                        isDark
                          ? "bg-[#2A2A2A] border-[#383838]"
                          : "bg-[#FAFAFA] border-[#ECECEC]"
                      }`}
                    >
                      <th className="py-2.5 px-4">Invoice #</th>
                      <th className="py-2.5 px-4">Customer Entity</th>
                      <th className="py-2.5 px-4">Date</th>
                      <th className="py-2.5 px-4">Tender Type</th>
                      <th className="py-2.5 px-4 text-right">Total Amount</th>
                      <th className="py-2.5 px-4">Status</th>
                      <th className="py-2.5 px-4 text-center">Action</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-current/10">
                    {sampleLedger.map((row) => (
                      <tr
                        key={row.id}
                        className={`transition-colors ${
                          isDark ? "hover:bg-[#2E2E2E]" : "hover:bg-[#F7F7F7]"
                        }`}
                      >
                        <td className="py-3 px-4 font-mono font-semibold">
                          <span
                            className="hover:underline cursor-pointer"
                            style={{ color: currentAccent.accent }}
                          >
                            {row.id}
                          </span>
                        </td>
                        <td className="py-3 px-4 font-medium">{row.customer}</td>
                        <td className="py-3 px-4 opacity-70">{row.date}</td>
                        <td className="py-3 px-4 opacity-80">{row.payment}</td>
                        <td className="py-3 px-4 text-right font-mono font-bold">
                          {row.amount}
                        </td>
                        <td className="py-3 px-4">
                          <span
                            className={`px-2 py-0.5 rounded text-[10px] font-semibold inline-flex items-center gap-1 ${
                              row.status === "Completed"
                                ? isDark
                                  ? "bg-emerald-500/15 text-emerald-400 border border-emerald-500/30"
                                  : "bg-emerald-50 text-emerald-700 border border-emerald-200"
                                : row.status === "Processing"
                                ? isDark
                                  ? "bg-blue-500/15 text-blue-400 border border-blue-500/30"
                                  : "bg-blue-50 text-blue-700 border border-blue-200"
                                : isDark
                                ? "bg-amber-500/15 text-amber-400 border border-amber-500/30"
                                : "bg-amber-50 text-amber-700 border border-amber-200"
                            }`}
                          >
                            <span className="w-1.5 h-1.5 rounded-full bg-current" />
                            <span>{row.status}</span>
                          </span>
                        </td>
                        <td className="py-3 px-4 text-center">
                          <button
                            className={`h-7 px-2.5 rounded text-[11px] font-medium border transition-colors ${
                              isDark
                                ? "bg-[#2D2D2D] hover:bg-[#383838] border-[#3D3D3D]"
                                : "bg-[#FFFFFF] hover:bg-[#F2F2F2] border-[#D9D9D9]"
                            }`}
                          >
                            View
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>

            {/* =====================================================================
                SECTION 4: COMPARISON MATRIX (APPLE VS WINDOWS 11 VS TACTILE)
                ===================================================================== */}
            <div
              className={`rounded-lg border p-5 transition-colors ${
                isDark
                  ? "bg-[#262626] border-[#363636]"
                  : "bg-[#FFFFFF] border-[#E3E3E3] shadow-[0_2px_4px_rgba(0,0,0,0.02)]"
              }`}
            >
              <h3 className="text-sm font-bold mb-3">Theme Comparison for Karobar ERP</h3>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                {/* Cupertino Glass */}
                <div
                  className={`p-3.5 rounded-lg border text-xs space-y-1.5 ${
                    isDark ? "bg-[#202020] border-[#333333]" : "bg-[#F8F8F8] border-[#E0E0E0]"
                  }`}
                >
                  <span className="font-bold text-blue-500 block">Theme A: Cupertino Glass</span>
                  <p className="opacity-75 text-[11px] leading-relaxed">
                    macOS Liquid Glass, 24px blurs, 16px soft squircles, specular sheen highlight line.
                  </p>
                </div>

                {/* Tactile Clay */}
                <div
                  className={`p-3.5 rounded-lg border text-xs space-y-1.5 ${
                    isDark ? "bg-[#202020] border-[#333333]" : "bg-[#F8F8F8] border-[#E0E0E0]"
                  }`}
                >
                  <span className="font-bold text-indigo-400 block">Theme B: Tactile Clay</span>
                  <p className="opacity-75 text-[11px] leading-relaxed">
                    Teenage Engineering hardware, 3D push keys, sculpted matte clay, recessed wells, zero blur.
                  </p>
                </div>

                {/* Windows 11 */}
                <div
                  className="p-3.5 rounded-lg border text-xs space-y-1.5"
                  style={{
                    backgroundColor: currentAccent.subtle,
                    borderColor: currentAccent.border,
                  }}
                >
                  <span className="font-bold block" style={{ color: currentAccent.accent }}>
                    Theme C: Windows 11 Fluent (Proposed)
                  </span>
                  <p className="opacity-80 text-[11px] leading-relaxed">
                    WinUI 3 standard, Mica canvas, 4px-8px crisp geometry, bottom focus lines, micro-steppers.
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* =========================================================================
          CONTENTDIALOG (WINDOWS 11 MODAL POPUP PREVIEW)
          ========================================================================= */}
      {showModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/50 backdrop-blur-xs">
          <div
            className={`w-full max-w-md rounded-lg border p-6 space-y-5 transition-all shadow-2xl animate-in fade-in zoom-in-95 duration-150 ${
              isDark
                ? "bg-[#2C2C2C] border-[#404040] text-[#E0E0E0]"
                : "bg-[#FFFFFF] border-[#D1D1D1] text-[#1B1B1B]"
            }`}
          >
            <div className="flex items-center justify-between pb-3 border-b border-current/10">
              <h3 className="text-base font-bold">ContentDialog — New Transaction</h3>
              <button
                onClick={() => setShowModal(false)}
                className="p-1 rounded hover:bg-black/10 dark:hover:bg-white/10"
              >
                <XMarkIcon className="w-4 h-4" />
              </button>
            </div>

            <div className="space-y-3 text-xs">
              <p className="opacity-80 leading-relaxed">
                Confirm processing voucher <strong>INV-8005</strong> for customer entity{" "}
                <strong>{demoCustomer}</strong>?
              </p>
              <div
                className={`p-3 rounded border font-mono text-xs ${
                  isDark ? "bg-[#222222] border-[#383838]" : "bg-[#F7F7F7] border-[#E0E0E0]"
                }`}
              >
                <div className="flex justify-between py-0.5">
                  <span className="opacity-60">Item:</span>
                  <span className="font-bold">{demoItemName}</span>
                </div>
                <div className="flex justify-between py-0.5">
                  <span className="opacity-60">Qty & Price:</span>
                  <span>
                    {demoQty} × PKR {demoPrice}
                  </span>
                </div>
                <div className="flex justify-between py-0.5 border-t border-current/10 mt-1 pt-1 font-bold">
                  <span>Gross Payable:</span>
                  <span style={{ color: currentAccent.accent }}>PKR 98,000</span>
                </div>
              </div>
            </div>

            {/* ContentDialog Footer Buttons */}
            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setShowModal(false)}
                className={`h-8 px-4 rounded text-xs font-medium border transition-colors ${
                  isDark
                    ? "bg-[#333333] hover:bg-[#3D3D3D] border-[#444444]"
                    : "bg-[#FFFFFF] hover:bg-[#F2F2F2] border-[#D9D9D9]"
                }`}
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={() => {
                  setShowModal(false);
                }}
                className="h-8 px-4 rounded text-xs font-semibold text-white transition-all active:scale-[0.98]"
                style={{
                  backgroundColor: currentAccent.accent,
                  color: currentAccent.accentText,
                  borderBottom: "2px solid rgba(0,0,0,0.22)",
                }}
              >
                Commit Voucher
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
