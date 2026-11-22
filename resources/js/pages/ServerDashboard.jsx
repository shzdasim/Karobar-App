import React from "react";
import { useNavigate } from "react-router-dom";
import { ServerStackIcon, ArrowLeftIcon, ComputerDesktopIcon } from "@heroicons/react/24/solid";
import NetworkTerminalSettings from "@/components/settings/NetworkTerminalSettings.jsx";

export default function ServerDashboard() {
  const navigate = useNavigate();

  return (
    <div className="p-4 sm:p-6 max-w-7xl mx-auto space-y-5">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-2xl bg-blue-600 text-white shadow-sm">
            <ServerStackIcon className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-lg sm:text-xl font-extrabold text-gray-900 dark:text-white">
              Server &amp; Attached Terminals Dashboard
            </h1>
            <p className="text-xs text-gray-500 dark:text-slate-400">
              Monitor connected POS counter PCs, cash drawer shifts, LAN IP endpoints, and 1-Click Database Import / Export.
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => navigate("/sale-invoices/pos")}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold bg-blue-600 text-white hover:bg-blue-700 transition"
          >
            <ComputerDesktopIcon className="w-4 h-4" />
            Open POS Terminal
          </button>
          <button
            type="button"
            onClick={() => navigate("/dashboard")}
            className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-semibold border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 text-gray-700 dark:text-slate-200 hover:bg-gray-50"
          >
            <ArrowLeftIcon className="w-4 h-4" />
            Main Dashboard
          </button>
        </div>
      </div>

      <NetworkTerminalSettings />
    </div>
  );
}
