import React, { useCallback } from "react";
import { SunIcon, MoonIcon } from "@heroicons/react/24/solid";
import { useTheme } from "../context/ThemeContext.jsx";

export default function ThemeToggle({ className = "" }) {
  const { toggleTheme, isDark, isTransitioning } = useTheme();

  const handleClick = useCallback((e) => {
    e.preventDefault();
    e.stopPropagation();
    toggleTheme(e.currentTarget);
  }, [toggleTheme]);

  return (
    <button
      type="button"
      data-theme-toggle-btn="true"
      onClick={handleClick}
      disabled={isTransitioning}
      aria-pressed={isDark}
      className={`
        group relative inline-flex h-9 w-9 items-center justify-center rounded-full transition-all duration-200 ease-in-out
        focus:outline-hidden focus:ring-2 focus:ring-blue-400/50 focus:ring-offset-2 cursor-pointer z-50
        ${isDark 
          ? "bg-slate-700 ring-1 ring-slate-600/50 hover:ring-amber-400/60" 
          : "bg-linear-to-br from-amber-100 via-white to-amber-50 ring-1 ring-amber-200/50 hover:ring-indigo-400/60"
        }
        ${isTransitioning ? "scale-95 opacity-80" : "hover:scale-105"}
        ${className}
      `}
      title={isDark ? "Pull curtain up for Light Mode" : "Pull curtain down for Dark Mode"}
      aria-label={isDark ? "Switch to light mode" : "Switch to dark mode"}
    >
      {/* Subtle mini hanging cord indicator at top of toggle button */}
      <span
        aria-hidden="true"
        className="pointer-events-none absolute -top-2 left-1/2 -translate-x-1/2 flex flex-col items-center opacity-70 group-hover:opacity-100 group-hover:-translate-y-0.5 transition-all duration-200"
      >
        <span className="h-2 w-0.5 bg-amber-500 dark:bg-amber-400 rounded-full" />
        <span className="h-1.5 w-1.5 rounded-full bg-amber-400 border border-amber-700/60 shadow-2xs" />
      </span>

      {/* Sun Icon - visible in light mode */}
      <SunIcon
        className={`
          relative z-10 h-5 w-5 text-amber-500 transition-all duration-300
          ${isDark ? "opacity-0 rotate-90 scale-50" : "opacity-100 rotate-0 scale-100"}
        `}
      />

      {/* Moon Icon - visible in dark mode */}
      <MoonIcon
        className={`
          absolute h-5 w-5 text-indigo-200 transition-all duration-300
          ${isDark ? "opacity-100 rotate-0 scale-100" : "opacity-0 -rotate-90 scale-50"}
        `}
      />
    </button>
  );
}


