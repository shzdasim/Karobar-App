import React, { useCallback, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import axios from "axios";
import toast from "react-hot-toast";
import {
  ServerStackIcon,
  ComputerDesktopIcon,
  ArrowPathIcon,
  CheckCircleIcon,
  XCircleIcon,
  ExclamationTriangleIcon,
  ClipboardDocumentIcon,
  CircleStackIcon,
  ArrowDownTrayIcon,
  ArrowUpTrayIcon,
  ShieldCheckIcon,
  NoSymbolIcon,
  TrashIcon,
  PencilSquareIcon,
  WifiIcon,
  BanknotesIcon,
  DocumentTextIcon,
  WrenchScrewdriverIcon,
  BoltIcon,
  PlusCircleIcon,
  CommandLineIcon,
} from "@heroicons/react/24/solid";
import { useCurrency } from "@/context/CurrencyContext.jsx";

export default function NetworkTerminalSettings() {
  const navigate = useNavigate();
  const { formatCurrency } = useCurrency();

  const [activeSection, setActiveSection] = useState("all"); // all | terminals | database | troubleshoot
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [serverInfo, setServerInfo] = useState(null);
  const [terminals, setTerminals] = useState([]);
  const [currentTerminal, setCurrentTerminal] = useState(null);

  // Connect New Terminal Wizard state
  const [newTerminalName, setNewTerminalName] = useState("Counter 2");
  const [pingTesting, setPingTesting] = useState(false);
  const [pingResult, setPingResult] = useState(null);

  // Troubleshooter & Diagnostics state
  const [diagLoading, setDiagLoading] = useState(false);
  const [diagnostics, setDiagnostics] = useState(null);
  const [runningRepair, setRunningRepair] = useState(null);

  // Inline rename state
  const [editingId, setEditingId] = useState(null);
  const [editingName, setEditingName] = useState("");

  // Desktop mode switcher state (for Windows Standalone / Terminal PCs)
  const isElectronDesktop = Boolean(typeof window !== "undefined" && window.karobarDesktop?.isDesktop);
  const [desktopNetCfg, setDesktopNetCfg] = useState({
    mode: "standalone",
    serverUrl: "http://192.168.1.10:18960",
    terminalId: "",
    terminalName: "Counter 1",
    pairingToken: "",
  });
  const [testingConn, setTestingConn] = useState(false);
  const [connTestResult, setConnTestResult] = useState(null);
  const [savingDesktopCfg, setSavingDesktopCfg] = useState(false);

  // 1-Click Database Import & Export state
  const [exportingFormat, setExportingFormat] = useState(null);
  const [importFile, setImportFile] = useState(null);
  const [importMode, setImportMode] = useState("replace");
  const [importConflict, setImportConflict] = useState("update");
  const [includeSettingsOnImport, setIncludeSettingsOnImport] = useState(false);
  const [importingDb, setImportingDb] = useState(false);
  const [importReport, setImportReport] = useState(null);
  const fileInputRef = useRef(null);

  const fetchTerminalsAndServer = useCallback(async (silent = false) => {
    if (!silent) setRefreshing(true);
    try {
      const { data } = await axios.get("/api/terminals", {
        headers: { "X-Skip-Cache": "1" },
      });
      setServerInfo(data?.server || null);
      setTerminals(Array.isArray(data?.terminals) ? data.terminals : []);
      setCurrentTerminal(data?.current_terminal || null);
    } catch (err) {
      if (!silent) {
        toast.error(err?.response?.data?.message || "Could not load server & terminals status.");
      }
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, []);

  const fetchDiagnostics = useCallback(async (silent = false) => {
    if (!silent) setDiagLoading(true);
    try {
      const { data } = await axios.get("/api/server/diagnostics", {
        headers: { "X-Skip-Cache": "1" },
      });
      setDiagnostics(data);
    } catch (err) {
      if (!silent) {
        toast.error(err?.response?.data?.message || "Failed to run server diagnostics.");
      }
    } finally {
      setDiagLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchTerminalsAndServer(false);
    fetchDiagnostics(true);

    const interval = window.setInterval(() => fetchTerminalsAndServer(true), 5000);
    const onTerminalsChanged = () => fetchTerminalsAndServer(true);
    window.addEventListener("karobar:terminals-changed", onTerminalsChanged);

    if (isElectronDesktop && window.karobarDesktop?.getNetworkConfig) {
      window.karobarDesktop
        .getNetworkConfig()
        .then((cfg) => {
          if (cfg) {
            setDesktopNetCfg({
              mode: cfg.mode || "standalone",
              serverUrl: cfg.serverUrl || "http://192.168.1.10:18960",
              terminalId: cfg.terminalId || "",
              terminalName: cfg.terminalName || "Counter 1",
              pairingToken: cfg.pairingToken || "",
            });
          }
        })
        .catch(() => {});
    }

    return () => {
      window.clearInterval(interval);
      window.removeEventListener("karobar:terminals-changed", onTerminalsChanged);
    };
  }, [fetchTerminalsAndServer, fetchDiagnostics, isElectronDesktop]);

  const handleCopyText = (text, label = "Copied to clipboard") => {
    if (!text) return;
    navigator.clipboard?.writeText(text);
    toast.success(label);
  };

  const handleRunLivePing = async () => {
    setPingTesting(true);
    setPingResult(null);
    const startMs = performance.now();
    try {
      const { data } = await axios.post("/api/terminals/ping", {});
      const latencyMs = Math.max(1, Math.round(performance.now() - startMs));
      setPingResult({
        ok: true,
        latencyMs,
        storeName: data?.store_name || "Karobar Store Server",
        dbDriver: data?.db_driver || serverInfo?.db_driver || "mariadb",
        appVersion: data?.app_version || "2.0",
      });
      toast.success(`Server handshake verified (${latencyMs} ms)!`);
      fetchTerminalsAndServer(true);
    } catch (err) {
      setPingResult({
        ok: false,
        error: err?.response?.data?.message || err?.message || "Handshake failed.",
      });
      toast.error("Handshake test failed.");
    } finally {
      setPingTesting(false);
    }
  };

  const handleDownloadTerminalConfig = async () => {
    try {
      const response = await axios.get("/api/server/terminal-config", {
        params: { name: newTerminalName || "Counter 2" },
        responseType: "blob",
      });
      const slug = (newTerminalName || "counter-2").toLowerCase().replace(/[^a-z0-9]+/g, "-");
      const filename = `network-config-${slug}.json`;
      const blobUrl = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement("a");
      link.href = blobUrl;
      link.setAttribute("download", filename);
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(blobUrl);
      toast.success(`Downloaded ${filename} for ${newTerminalName}!`);
    } catch (err) {
      toast.error("Failed to download terminal config file.");
    }
  };

  const handleRunTroubleshootAction = async (action, label) => {
    setRunningRepair(action);
    const toastId = toast.loading(`Running: ${label}...`);
    try {
      const { data } = await axios.post("/api/server/troubleshoot", { action });
      toast.success(data?.message || `${label} completed!`, { id: toastId });
      await Promise.all([fetchTerminalsAndServer(true), fetchDiagnostics(true)]);
    } catch (err) {
      toast.error(err?.response?.data?.message || `Failed to run ${label}.`, { id: toastId });
    } finally {
      setRunningRepair(null);
    }
  };

  const handleToggleAutoAuthorize = async () => {
    if (!serverInfo) return;
    const nextVal = !serverInfo.terminal_auto_authorize;
    try {
      const { data } = await axios.put("/api/terminals/settings/network", {
        terminal_auto_authorize: nextVal,
      });
      setServerInfo((prev) => ({
        ...prev,
        terminal_auto_authorize: Boolean(data?.terminal_auto_authorize),
      }));
      toast.success(
        nextVal
          ? "New LAN terminals will now be authorized automatically."
          : "New LAN terminals will now require manual administrator approval."
      );
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to update network setting.");
    }
  };

  const handleRotateToken = async () => {
    try {
      const { data } = await axios.put("/api/terminals/settings/network", {
        regenerate_token: true,
      });
      setServerInfo((prev) => ({
        ...prev,
        network_server_token: data?.network_server_token,
      }));
      toast.success("Server pairing token rotated.");
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to rotate pairing token.");
    }
  };

  const handleRenameTerminal = async (termId) => {
    const cleanName = editingName.trim();
    if (!cleanName) return;
    try {
      await axios.put(`/api/terminals/${termId}`, { name: cleanName });
      toast.success("Terminal renamed.");
      setEditingId(null);
      fetchTerminalsAndServer(true);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to rename terminal.");
    }
  };

  const handleToggleAuthorizeTerminal = async (term) => {
    const nextAuth = !term.is_authorized;
    try {
      await axios.put(`/api/terminals/${term.id}`, { is_authorized: nextAuth });
      toast.success(nextAuth ? `Authorized ${term.name}` : `Blocked ${term.name}`);
      fetchTerminalsAndServer(true);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to update terminal authorization.");
    }
  };

  const handleRemoveTerminal = async (term) => {
    if (!window.confirm(`Remove terminal "${term.name}" (${term.ip_address || term.terminal_uid}) from registry?`)) {
      return;
    }
    try {
      await axios.delete(`/api/terminals/${term.id}`);
      toast.success(`Removed ${term.name}`);
      fetchTerminalsAndServer(true);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to remove terminal.");
    }
  };

  const handleTestDesktopServer = async () => {
    if (!window.karobarDesktop?.testServerConnection) return;
    setTestingConn(true);
    setConnTestResult(null);
    try {
      const res = await window.karobarDesktop.testServerConnection({
        serverUrl: desktopNetCfg.serverUrl,
        terminalName: desktopNetCfg.terminalName,
        pairingToken: desktopNetCfg.pairingToken,
      });
      setConnTestResult(res);
      if (res?.ok) {
        toast.success(`Connected to ${res.storeName || "Store Server"} (${res.latencyMs}ms)!`);
      } else {
        toast.error(res?.error || "Could not reach server.");
      }
    } catch (err) {
      setConnTestResult({ ok: false, error: err?.message || "Connection test failed." });
    } finally {
      setTestingConn(false);
    }
  };

  const handleSaveDesktopMode = async () => {
    if (!window.karobarDesktop?.saveNetworkConfig) return;
    if (desktopNetCfg.mode === "terminal" && !desktopNetCfg.serverUrl.trim()) {
      toast.error("Please enter the Store Server LAN URL (e.g. http://192.168.1.10:18960).");
      return;
    }
    setSavingDesktopCfg(true);
    try {
      localStorage.setItem("karobar_terminal_name", desktopNetCfg.terminalName);
      localStorage.setItem("karobar_terminal_mode", desktopNetCfg.mode);
      if (desktopNetCfg.pairingToken?.trim()) {
        localStorage.setItem("karobar_pairing_token", desktopNetCfg.pairingToken.trim());
      } else {
        localStorage.removeItem("karobar_pairing_token");
      }
      await window.karobarDesktop.saveNetworkConfig({
        mode: desktopNetCfg.mode,
        serverUrl: desktopNetCfg.serverUrl,
        terminalName: desktopNetCfg.terminalName,
        pairingToken: desktopNetCfg.pairingToken || "",
        relaunch: true,
      });
      toast.success("Terminal network mode saved! Reconnecting...");
    } catch (err) {
      toast.error(err?.message || "Failed to save terminal network config.");
    } finally {
      setSavingDesktopCfg(false);
    }
  };

  const handleExportDatabase = async (format = "sqlite") => {
    setExportingFormat(format);
    const toastId = toast.loading(
      format === "sqlite"
        ? "Generating portable .sqlite database file..."
        : format === "zip"
        ? "Generating full .zip backup archive..."
        : "Generating SQL database dump..."
    );
    try {
      const response = await axios.get("/api/server/export-database", {
        params: { format },
        responseType: "blob",
        timeout: 300000,
      });

      const disposition = response.headers?.["content-disposition"] || "";
      let filename =
        format === "sqlite"
          ? `karobar-portable-${new Date().toISOString().slice(0, 10)}.sqlite`
          : format === "zip"
          ? `karobar-full-backup-${new Date().toISOString().slice(0, 10)}.zip`
          : `karobar-db-dump-${new Date().toISOString().slice(0, 10)}.sql.gz`;

      const match = disposition.match(/filename="?([^";]+)"?/i);
      if (match && match[1]) {
        filename = match[1];
      }

      const blobUrl = window.URL.createObjectURL(new Blob([response.data]));
      const link = document.createElement("a");
      link.href = blobUrl;
      link.setAttribute("download", filename);
      document.body.appendChild(link);
      link.click();
      link.remove();
      window.URL.revokeObjectURL(blobUrl);

      toast.success(`Exported ${filename} successfully!`, { id: toastId });
    } catch (err) {
      toast.error(err?.response?.data?.message || "Failed to export database.", { id: toastId });
    } finally {
      setExportingFormat(null);
    }
  };

  const handleQuickImportDatabase = async (e) => {
    e.preventDefault();
    if (!importFile) {
      toast.error("Please select a .sqlite, .db, .sql, .sql.gz, or .zip database file first.");
      return;
    }

    const confirmMsg =
      importMode === "replace"
        ? `Import "${importFile.name}" in REPLACE mode? (An automatic safety backup will be created first, and Users/Licenses will remain untouched.)`
        : `Import "${importFile.name}" in MERGE mode? (An automatic safety backup will be created first.)`;

    if (!window.confirm(confirmMsg)) return;

    setImportingDb(true);
    setImportReport(null);
    const toastId = toast.loading(`Importing ${importFile.name} into server database...`);

    try {
      const formData = new FormData();
      formData.append("file", importFile);
      formData.append("mode", importMode);
      formData.append("conflict_policy", importConflict);
      formData.append("include_settings", includeSettingsOnImport ? "1" : "0");

      const { data } = await axios.post("/api/server/quick-import-database", formData, {
        headers: { "Content-Type": "multipart/form-data" },
        timeout: 600000,
      });

      setImportReport(data);
      toast.success(data?.message || "Database imported successfully!", { id: toastId });
      setImportFile(null);
      if (fileInputRef.current) fileInputRef.current.value = "";
      fetchTerminalsAndServer(true);
      fetchDiagnostics(true);
    } catch (err) {
      toast.error(err?.response?.data?.message || "Database import failed.", { id: toastId });
    } finally {
      setImportingDb(false);
    }
  };

  if (loading) {
    return (
      <div className="p-6 rounded-2xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800/80 text-sm text-gray-500 dark:text-slate-400">
        Loading Server &amp; Attached Terminals Control Center…
      </div>
    );
  }

  const showTerminals = activeSection === "all" || activeSection === "terminals";
  const showDatabase = activeSection === "all" || activeSection === "database";
  const showTroubleshoot = activeSection === "all" || activeSection === "troubleshoot";

  const lanUrl = serverInfo?.recommended_url || window.location.origin;
  const mdnsUrl = `http://${serverInfo?.hostname || "karobar-server"}.local:${serverInfo?.port || 18960}`;

  return (
    <div className="space-y-6">
      {/* Top Section Switcher Pills */}
      <div className="flex flex-wrap items-center justify-between gap-3 bg-white dark:bg-slate-800/90 p-3 rounded-2xl border border-gray-200/90 dark:border-slate-700/80 shadow-sm">
        <div className="flex flex-wrap items-center gap-1.5">
          {[
            { id: "all", label: "All Controls", icon: ServerStackIcon },
            { id: "terminals", label: `Terminals & Connect (${terminals.length})`, icon: ComputerDesktopIcon },
            { id: "database", label: "1-Click DB Import / Export", icon: CircleStackIcon },
            { id: "troubleshoot", label: "Troubleshooter & Logs", icon: WrenchScrewdriverIcon },
          ].map((tab) => {
            const Icon = tab.icon;
            const active = activeSection === tab.id;
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => setActiveSection(tab.id)}
                className={`inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold transition ${
                  active
                    ? "bg-blue-600 text-white shadow-sm"
                    : "text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700/60"
                }`}
              >
                <Icon className="w-4 h-4" />
                {tab.label}
              </button>
            );
          })}
        </div>

        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => handleRunTroubleshootAction("force_sync_pulse", "Broadcast Force-Sync Pulse")}
            disabled={runningRepair !== null}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-bold bg-indigo-50 text-indigo-700 dark:bg-indigo-500/20 dark:text-indigo-300 hover:bg-indigo-100 transition"
            title="Immediately sync all connected POS counters"
          >
            <BoltIcon className="w-4 h-4" />
            Force Sync All Counters
          </button>

          <button
            type="button"
            onClick={() => {
              fetchTerminalsAndServer(false);
              fetchDiagnostics(false);
            }}
            disabled={refreshing || diagLoading}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold border border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-700/60 text-gray-700 dark:text-slate-200 hover:bg-gray-100 dark:hover:bg-slate-700 transition"
          >
            <ArrowPathIcon className={`w-4 h-4 ${refreshing || diagLoading ? "animate-spin" : ""}`} />
            Refresh All
          </button>
        </div>
      </div>

      {/* 1. Linux Server Health & LAN Identity Card */}
      <div className="rounded-2xl border border-gray-200/90 dark:border-slate-700/80 bg-white dark:bg-slate-800/90 p-5 shadow-sm">
        <div className="flex flex-wrap items-center justify-between gap-4 pb-4 border-b border-gray-100 dark:border-slate-700/70">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-xl bg-blue-600/10 text-blue-600 dark:bg-blue-500/20 dark:text-blue-400">
              <ServerStackIcon className="w-6 h-6" />
            </div>
            <div>
              <div className="flex items-center gap-2.5">
                <h3 className="text-base font-bold text-gray-900 dark:text-slate-100">
                  Store Server &amp; LAN Synchronization Hub
                </h3>
                <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-100 text-emerald-800 dark:bg-emerald-500/20 dark:text-emerald-300">
                  <span className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
                  Server Active
                </span>
              </div>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                Host: <span className="font-semibold text-gray-700 dark:text-slate-200">{serverInfo?.hostname || "localhost"}</span>{" "}
                • OS: <span className="font-semibold text-gray-700 dark:text-slate-200">{serverInfo?.os_release || serverInfo?.os_family}</span>{" "}
                • Engine: <span className="font-semibold text-blue-600 dark:text-blue-400">{serverInfo?.db_version || serverInfo?.db_driver}</span>{" "}
                • Cache: <span className="font-semibold text-gray-700 dark:text-slate-200">{String(serverInfo?.cache_driver || "file").toUpperCase()}</span>
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handleRunLivePing}
              disabled={pingTesting}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-bold bg-emerald-600 text-white hover:bg-emerald-700 transition"
            >
              <WifiIcon className="w-4 h-4" />
              {pingTesting ? "Pinging…" : "Test Server Handshake"}
            </button>
          </div>
        </div>

        {pingResult && (
          <div
            className={`mt-3 p-3 rounded-xl border text-xs flex items-center justify-between ${
              pingResult.ok
                ? "bg-emerald-50/80 border-emerald-200 text-emerald-800 dark:bg-emerald-500/15 dark:border-emerald-500/30 dark:text-emerald-200"
                : "bg-red-50 border-red-200 text-red-700 dark:bg-red-500/15 dark:border-red-500/30 dark:text-red-300"
            }`}
          >
            <div className="flex items-center gap-2 font-semibold">
              {pingResult.ok ? (
                <>
                  <CheckCircleIcon className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
                  <span>
                    Handshake Verified: <strong>{pingResult.storeName}</strong> • Database:{" "}
                    <strong>{String(pingResult.dbDriver).toUpperCase()}</strong> • Roundtrip Latency:{" "}
                    <strong>{pingResult.latencyMs} ms</strong>
                  </span>
                </>
              ) : (
                <>
                  <XCircleIcon className="w-4 h-4 text-red-600" />
                  <span>{pingResult.error}</span>
                </>
              )}
            </div>
            <button
              type="button"
              onClick={() => setPingResult(null)}
              className="text-[11px] underline opacity-75 hover:opacity-100"
            >
              Dismiss
            </button>
          </div>
        )}

        {/* Metrics & Connection URL Grid */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mt-4">
          <div className="p-3.5 rounded-xl bg-slate-50 dark:bg-slate-900/60 border border-slate-200/80 dark:border-slate-700/70">
            <div className="text-[11px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
              Terminal Connection URL (LAN IP &amp; mDNS)
            </div>
            <div className="mt-1.5 flex items-center justify-between gap-2">
              <code className="text-sm font-bold text-blue-600 dark:text-blue-400 truncate">
                {lanUrl}
              </code>
              <button
                type="button"
                onClick={() => handleCopyText(lanUrl, "Server LAN URL copied!")}
                className="p-1.5 rounded-lg bg-blue-50 dark:bg-blue-500/20 text-blue-600 dark:text-blue-300 hover:bg-blue-100"
                title="Copy LAN URL"
              >
                <ClipboardDocumentIcon className="w-4 h-4" />
              </button>
            </div>
            <div className="mt-1 flex items-center justify-between text-[11px] text-gray-500 dark:text-slate-400">
              <span className="truncate">mDNS: {mdnsUrl}</span>
              <button
                type="button"
                onClick={() => handleCopyText(mdnsUrl, "mDNS URL copied!")}
                className="text-blue-600 dark:text-blue-400 hover:underline ml-2 shrink-0"
              >
                Copy .local
              </button>
            </div>
          </div>

          <div className="p-3.5 rounded-xl bg-slate-50 dark:bg-slate-900/60 border border-slate-200/80 dark:border-slate-700/70">
            <div className="text-[11px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
              Attached Terminals &amp; License Seats
            </div>
            <div className="mt-1.5 flex items-baseline gap-2">
              <span className="text-xl font-extrabold text-gray-900 dark:text-white">
                {serverInfo?.online_terminals_count ?? 0} Online
              </span>
              <span className="text-xs text-gray-500 dark:text-slate-400">
                / {serverInfo?.total_terminals_count ?? terminals.length} Registered
                {serverInfo?.seats_limit ? ` (Max ${serverInfo.seats_limit} Seats)` : " (Unlimited Seats)"}
              </span>
            </div>
            <div className="mt-1.5 flex items-center gap-2">
              <label className="inline-flex items-center gap-1.5 text-xs text-gray-600 dark:text-slate-300 cursor-pointer">
                <input
                  type="checkbox"
                  checked={Boolean(serverInfo?.terminal_auto_authorize ?? true)}
                  onChange={handleToggleAutoAuthorize}
                  className="rounded border-gray-300 text-blue-600 focus:ring-blue-500"
                />
                Auto-authorize new LAN terminals
              </label>
            </div>
          </div>

          <div className="p-3.5 rounded-xl bg-slate-50 dark:bg-slate-900/60 border border-slate-200/80 dark:border-slate-700/70">
            <div className="flex items-center justify-between">
              <span className="text-[11px] font-semibold uppercase tracking-wider text-gray-500 dark:text-slate-400">
                Pairing Token &amp; Daemons
              </span>
              <button
                type="button"
                onClick={handleRotateToken}
                className="text-[10px] font-semibold text-blue-600 dark:text-blue-400 hover:underline"
              >
                Rotate Token
              </button>
            </div>
            <div className="mt-1 flex items-center justify-between gap-2">
              <code className="text-xs font-mono font-bold text-gray-800 dark:text-slate-200">
                {serverInfo?.network_server_token || "KRB-NET-READY"}
              </code>
              <button
                type="button"
                onClick={() => handleCopyText(serverInfo?.network_server_token, "Pairing token copied!")}
                className="text-[11px] text-blue-600 dark:text-blue-400 hover:underline"
              >
                Copy
              </button>
            </div>
            <div className="mt-2 flex flex-wrap gap-1.5">
              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-medium bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">
                <CheckCircleIcon className="w-3 h-3" />
                DB ({serverInfo?.db_driver || "sql"})
              </span>
              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-md text-[11px] font-medium bg-emerald-50 text-emerald-700 dark:bg-emerald-500/15 dark:text-emerald-300">
                <CheckCircleIcon className="w-3 h-3" />
                SyncPulse ({serverInfo?.cache_driver || "cache"})
              </span>
            </div>
          </div>
        </div>
      </div>

      {/* 2. Connect a New Terminal PC Wizard + Attached Terminals Live Matrix */}
      {showTerminals && (
        <>
          {/* Connect a New Terminal Wizard Card */}
          <div className="rounded-2xl border border-indigo-200/80 dark:border-indigo-500/30 bg-indigo-50/40 dark:bg-slate-800/90 p-5 shadow-sm">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div className="flex items-center gap-2.5">
                <PlusCircleIcon className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
                <div>
                  <h3 className="text-sm sm:text-base font-bold text-gray-900 dark:text-slate-100">
                    Connect a New POS Counter / Terminal PC in 30 Seconds
                  </h3>
                  <p className="text-xs text-gray-600 dark:text-slate-400">
                    On any Windows Counter PC, run the Karobar App Installer and select{" "}
                    <strong>&ldquo;Multi-PC POS Terminal&rdquo;</strong> with URL{" "}
                    <code className="font-bold text-indigo-600 dark:text-indigo-300">{lanUrl}</code>, OR download a pre-configured{" "}
                    <code className="font-mono">network-config.json</code> below.
                  </p>
                </div>
              </div>

              <div className="flex flex-wrap items-center gap-2">
                <input
                  type="text"
                  value={newTerminalName}
                  onChange={(e) => setNewTerminalName(e.target.value)}
                  placeholder="Counter 2 — Pharmacy"
                  className="px-3 py-1.5 rounded-xl border border-indigo-200 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs font-semibold text-gray-800 dark:text-white"
                />
                <button
                  type="button"
                  onClick={handleDownloadTerminalConfig}
                  className="inline-flex items-center gap-1.5 px-3.5 py-2 rounded-xl text-xs font-bold bg-indigo-600 text-white hover:bg-indigo-700 transition"
                >
                  <ArrowDownTrayIcon className="w-4 h-4" />
                  Download Terminal Config (.json)
                </button>
              </div>
            </div>
          </div>

          {/* Attached Terminals Live Matrix */}
          <div className="rounded-2xl border border-gray-200/90 dark:border-slate-700/80 bg-white dark:bg-slate-800/90 p-5 shadow-sm">
            <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
              <div className="flex items-center gap-2.5">
                <ComputerDesktopIcon className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
                <div>
                  <h3 className="text-sm sm:text-base font-bold text-gray-900 dark:text-slate-100">
                    Attached POS Terminals &amp; Counters ({terminals.length})
                  </h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400">
                    Live view of all connected Windows Terminal PCs, active cashiers, open cash drawers, and today&apos;s billing activity.
                  </p>
                </div>
              </div>
            </div>

            {terminals.length === 0 ? (
              <div className="p-6 text-center rounded-xl border border-dashed border-gray-200 dark:border-slate-700 text-xs text-gray-500 dark:text-slate-400">
                No remote terminals registered yet. Click <strong>&ldquo;Test Server Handshake&rdquo;</strong> above or connect a Windows Terminal PC using{" "}
                <code className="font-bold text-blue-600 dark:text-blue-400">{lanUrl}</code> and it will appear here automatically.
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-left border-collapse text-xs">
                  <thead>
                    <tr className="border-b border-gray-200 dark:border-slate-700 text-[11px] font-semibold uppercase text-gray-500 dark:text-slate-400">
                      <th className="py-2.5 px-3">Terminal / Counter</th>
                      <th className="py-2.5 px-3">LAN IP &amp; Host</th>
                      <th className="py-2.5 px-3">Status</th>
                      <th className="py-2.5 px-3">Active Cashier</th>
                      <th className="py-2.5 px-3">Open Cash Drawer</th>
                      <th className="py-2.5 px-3">Today&apos;s Billing</th>
                      <th className="py-2.5 px-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-gray-100 dark:divide-slate-700/60">
                    {terminals.map((term) => {
                      const isCurrent = currentTerminal && currentTerminal.id === term.id;
                      return (
                        <tr
                          key={term.id}
                          className="hover:bg-gray-50/80 dark:hover:bg-slate-700/30 transition"
                        >
                          <td className="py-3 px-3">
                            {editingId === term.id ? (
                              <div className="flex items-center gap-1.5">
                                <input
                                  type="text"
                                  value={editingName}
                                  onChange={(e) => setEditingName(e.target.value)}
                                  className="px-2 py-1 text-xs rounded-lg border border-blue-400 bg-white dark:bg-slate-900 text-gray-900 dark:text-white"
                                  autoFocus
                                />
                                <button
                                  type="button"
                                  onClick={() => handleRenameTerminal(term.id)}
                                  className="px-2 py-1 rounded-lg bg-blue-600 text-white font-semibold"
                                >
                                  Save
                                </button>
                                <button
                                  type="button"
                                  onClick={() => setEditingId(null)}
                                  className="px-2 py-1 rounded-lg bg-gray-200 dark:bg-slate-700 text-gray-700 dark:text-slate-300"
                                >
                                  Cancel
                                </button>
                              </div>
                            ) : (
                              <div className="flex items-center gap-2">
                                <div>
                                  <div className="font-bold text-gray-900 dark:text-slate-100 flex items-center gap-1.5">
                                    <span>{term.name}</span>
                                    {isCurrent && (
                                      <span className="px-1.5 py-0.5 text-[10px] rounded bg-blue-100 text-blue-700 dark:bg-blue-500/20 dark:text-blue-300">
                                        This Device
                                      </span>
                                    )}
                                  </div>
                                  <div className="text-[11px] text-gray-400 font-mono">
                                    {term.terminal_uid} • {String(term.mode || "terminal").toUpperCase()}
                                  </div>
                                </div>
                                <button
                                  type="button"
                                  onClick={() => {
                                    setEditingId(term.id);
                                    setEditingName(term.name);
                                  }}
                                  className="p-1 text-gray-400 hover:text-blue-600"
                                  title="Rename Terminal"
                                >
                                  <PencilSquareIcon className="w-3.5 h-3.5" />
                                </button>
                              </div>
                            )}
                          </td>

                          <td className="py-3 px-3">
                            <div className="font-mono font-semibold text-gray-800 dark:text-slate-200">
                              {term.ip_address || "—"}
                            </div>
                            <div className="text-[11px] text-gray-500 dark:text-slate-400">
                              {term.hostname || term.os_platform || "LAN Client"}
                            </div>
                          </td>

                          <td className="py-3 px-3">
                            {!term.is_authorized ? (
                              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-semibold bg-red-100 text-red-700 dark:bg-red-500/20 dark:text-red-300">
                                <NoSymbolIcon className="w-3 h-3" />
                                Blocked
                              </span>
                            ) : term.is_online ? (
                              <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-[11px] font-semibold bg-emerald-100 text-emerald-800 dark:bg-emerald-500/20 dark:text-emerald-300">
                                <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-ping" />
                                Online
                              </span>
                            ) : (
                              <div>
                                <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium bg-gray-100 text-gray-600 dark:bg-slate-700 dark:text-slate-300">
                                  Offline
                                </span>
                                <div className="text-[10px] text-gray-400 mt-0.5">
                                  {term.last_seen_human}
                                </div>
                              </div>
                            )}
                          </td>

                          <td className="py-3 px-3">
                            {term.current_user ? (
                              <div>
                                <div className="font-semibold text-gray-800 dark:text-slate-200">
                                  {term.current_user.name}
                                </div>
                                <div className="text-[11px] text-gray-400">
                                  {term.current_user.email}
                                </div>
                              </div>
                            ) : (
                              <span className="text-gray-400">—</span>
                            )}
                          </td>

                          <td className="py-3 px-3">
                            {term.open_drawer ? (
                              <div className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-lg bg-emerald-50 dark:bg-emerald-500/15 text-emerald-700 dark:text-emerald-300">
                                <BanknotesIcon className="w-3.5 h-3.5 shrink-0" />
                                <div>
                                  <div className="font-bold">
                                    {formatCurrency(term.open_drawer.expected_balance || 0)}
                                  </div>
                                  <div className="text-[10px] opacity-80">
                                    Session #{term.open_drawer.id} Open
                                  </div>
                                </div>
                              </div>
                            ) : (
                              <span className="text-gray-400">Closed</span>
                            )}
                          </td>

                          <td className="py-3 px-3">
                            <div className="font-semibold text-gray-900 dark:text-slate-100">
                              {term.today_invoices_count || 0} Invoices
                            </div>
                            <div className="text-[11px] text-gray-500 dark:text-slate-400">
                              {formatCurrency(term.today_sales_total || 0)}
                            </div>
                          </td>

                          <td className="py-3 px-3 text-right">
                            <div className="inline-flex items-center gap-1.5">
                              <button
                                type="button"
                                onClick={() => handleToggleAuthorizeTerminal(term)}
                                className={`inline-flex items-center gap-1 px-2.5 py-1 rounded-lg text-[11px] font-semibold transition ${
                                  term.is_authorized
                                    ? "bg-amber-50 text-amber-700 hover:bg-amber-100 dark:bg-amber-500/15 dark:text-amber-300"
                                    : "bg-emerald-50 text-emerald-700 hover:bg-emerald-100 dark:bg-emerald-500/15 dark:text-emerald-300"
                                }`}
                              >
                                {term.is_authorized ? (
                                  <>
                                    <NoSymbolIcon className="w-3.5 h-3.5" />
                                    Block
                                  </>
                                ) : (
                                  <>
                                    <ShieldCheckIcon className="w-3.5 h-3.5" />
                                    Authorize
                                  </>
                                )}
                              </button>

                              <button
                                type="button"
                                onClick={() => handleRemoveTerminal(term)}
                                className="p-1.5 rounded-lg text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-500/10"
                                title="Remove terminal"
                              >
                                <TrashIcon className="w-4 h-4" />
                              </button>
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        </>
      )}

      {/* 3. 1-Click Database Import & Export Hub */}
      {showDatabase && (
        <div className="rounded-2xl border border-gray-200/90 dark:border-slate-700/80 bg-white dark:bg-slate-800/90 p-5 shadow-sm">
          <div className="flex items-center gap-2.5 pb-4 border-b border-gray-100 dark:border-slate-700/70">
            <CircleStackIcon className="w-5 h-5 text-emerald-600 dark:text-emerald-400" />
            <div>
              <h3 className="text-sm sm:text-base font-bold text-gray-900 dark:text-slate-100">
                Database Import &amp; Portable Export Hub
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400">
                Import an existing database (`.sqlite`, `.sql.gz`, `.zip`) into this Server or export a portable `.sqlite` / `.sql.gz` / `.zip` snapshot anytime.
              </p>
            </div>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mt-4">
            {/* Left Column: 1-Click Export */}
            <div className="p-4 rounded-xl bg-slate-50 dark:bg-slate-900/60 border border-slate-200/80 dark:border-slate-700/70 flex flex-col justify-between">
              <div>
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-gray-700 dark:text-slate-200">
                  <ArrowDownTrayIcon className="w-4 h-4 text-blue-600 dark:text-blue-400" />
                  Export Server Database (Download Anytime)
                </div>
                <p className="text-xs text-gray-500 dark:text-slate-400 mt-1.5 leading-relaxed">
                  Download your complete live database in a single click. Exporting as{" "}
                  <strong>Portable SQLite (`.sqlite`)</strong> automatically converts MariaDB/MySQL tables into a self-contained SQLite file that can run on any Standalone Windows PC.
                </p>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-2.5 mt-4">
                <button
                  type="button"
                  onClick={() => handleExportDatabase("sqlite")}
                  disabled={exportingFormat !== null}
                  className="flex flex-col items-center justify-center p-3 rounded-xl border border-blue-200 dark:border-blue-500/40 bg-blue-50/80 dark:bg-blue-500/15 text-blue-700 dark:text-blue-300 hover:bg-blue-100 transition text-center"
                >
                  <CircleStackIcon className="w-5 h-5 mb-1" />
                  <span className="text-xs font-bold">
                    {exportingFormat === "sqlite" ? "Exporting…" : "Export .SQLITE"}
                  </span>
                  <span className="text-[10px] opacity-75">Portable Single-File</span>
                </button>

                <button
                  type="button"
                  onClick={() => handleExportDatabase("sql")}
                  disabled={exportingFormat !== null}
                  className="flex flex-col items-center justify-center p-3 rounded-xl border border-emerald-200 dark:border-emerald-500/40 bg-emerald-50/80 dark:bg-emerald-500/15 text-emerald-700 dark:text-emerald-300 hover:bg-emerald-100 transition text-center"
                >
                  <DocumentTextIcon className="w-5 h-5 mb-1" />
                  <span className="text-xs font-bold">
                    {exportingFormat === "sql" ? "Exporting…" : "Export .SQL.GZ"}
                  </span>
                  <span className="text-[10px] opacity-75">MariaDB / SQL Dump</span>
                </button>

                <button
                  type="button"
                  onClick={() => handleExportDatabase("zip")}
                  disabled={exportingFormat !== null}
                  className="flex flex-col items-center justify-center p-3 rounded-xl border border-purple-200 dark:border-purple-500/40 bg-purple-50/80 dark:bg-purple-500/15 text-purple-700 dark:text-purple-300 hover:bg-purple-100 transition text-center"
                >
                  <ArrowDownTrayIcon className="w-5 h-5 mb-1" />
                  <span className="text-xs font-bold">
                    {exportingFormat === "zip" ? "Exporting…" : "Export .ZIP"}
                  </span>
                  <span className="text-[10px] opacity-75">Full DB + Uploads</span>
                </button>
              </div>
            </div>

            {/* Right Column: 1-Click Import */}
            <form
              onSubmit={handleQuickImportDatabase}
              className="p-4 rounded-xl bg-slate-50 dark:bg-slate-900/60 border border-slate-200/80 dark:border-slate-700/70 space-y-3"
            >
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-gray-700 dark:text-slate-200">
                  <ArrowUpTrayIcon className="w-4 h-4 text-emerald-600 dark:text-emerald-400" />
                  Import Existing Database (.SQLITE / .SQL.GZ / .ZIP)
                </div>
                <button
                  type="button"
                  onClick={() => navigate("/settings/sqlite-import")}
                  className="text-[11px] font-semibold text-blue-600 dark:text-blue-400 hover:underline"
                >
                  Advanced Column Mapper →
                </button>
              </div>

              <input
                ref={fileInputRef}
                type="file"
                accept=".sqlite,.sqlite3,.db,.db3,.sql,.gz,.zip"
                onChange={(e) => setImportFile(e.target.files?.[0] || null)}
                className="block w-full text-xs text-gray-700 dark:text-slate-200 file:mr-3 file:py-1.5 file:px-3 file:rounded-lg file:border-0 file:text-xs file:font-semibold file:bg-blue-600 file:text-white hover:file:bg-blue-700"
              />

              <div className="grid grid-cols-2 gap-2 text-xs">
                <div>
                  <label className="block text-[11px] font-semibold text-gray-500 dark:text-slate-400 mb-1">
                    Import Mode
                  </label>
                  <select
                    value={importMode}
                    onChange={(e) => setImportMode(e.target.value)}
                    className="w-full px-2.5 py-1.5 rounded-lg border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs"
                  >
                    <option value="replace">Replace Business Data (Clean Import)</option>
                    <option value="merge">Merge / Upsert with Current Data</option>
                  </select>
                </div>

                <div>
                  <label className="block text-[11px] font-semibold text-gray-500 dark:text-slate-400 mb-1">
                    Duplicate Records
                  </label>
                  <select
                    value={importConflict}
                    onChange={(e) => setImportConflict(e.target.value)}
                    className="w-full px-2.5 py-1.5 rounded-lg border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs"
                  >
                    <option value="update">Update Existing Records</option>
                    <option value="skip">Skip Duplicates</option>
                  </select>
                </div>
              </div>

              <div className="flex items-center justify-between gap-2 pt-1">
                <label className="inline-flex items-center gap-1.5 text-xs text-gray-600 dark:text-slate-300 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={includeSettingsOnImport}
                    onChange={(e) => setIncludeSettingsOnImport(e.target.checked)}
                    className="rounded border-gray-300 text-blue-600"
                  />
                  Include store profile settings
                </label>

                <button
                  type="submit"
                  disabled={importingDb || !importFile}
                  className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl text-xs font-bold bg-emerald-600 text-white hover:bg-emerald-700 disabled:opacity-50 transition"
                >
                  <ArrowUpTrayIcon className="w-4 h-4" />
                  {importingDb ? "Importing Database…" : "1-Click Import Database"}
                </button>
              </div>

              {importReport && (
                <div className="p-2.5 rounded-lg bg-emerald-50 dark:bg-emerald-500/15 border border-emerald-200 dark:border-emerald-500/30 text-xs text-emerald-800 dark:text-emerald-200">
                  {importReport.message}
                </div>
              )}
            </form>
          </div>
        </div>
      )}

      {/* 4. Full-Featured Server & LAN Troubleshooter + Live Log Viewer */}
      {showTroubleshoot && (
        <div className="rounded-2xl border border-gray-200/90 dark:border-slate-700/80 bg-white dark:bg-slate-800/90 p-5 shadow-sm space-y-5">
          <div className="flex flex-wrap items-center justify-between gap-3 pb-4 border-b border-gray-100 dark:border-slate-700/70">
            <div className="flex items-center gap-2.5">
              <WrenchScrewdriverIcon className="w-5 h-5 text-amber-600 dark:text-amber-400" />
              <div>
                <h3 className="text-sm sm:text-base font-bold text-gray-900 dark:text-slate-100">
                  Full-Featured Server &amp; LAN Troubleshooter
                </h3>
                <p className="text-xs text-gray-500 dark:text-slate-400">
                  Live health checks, 1-click self-healing repairs, atomic sequence verification, and real-time server logs.
                </p>
              </div>
            </div>

            <button
              type="button"
              onClick={() => fetchDiagnostics(false)}
              disabled={diagLoading}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold border border-gray-200 dark:border-slate-700 bg-gray-50 dark:bg-slate-700/60 text-gray-700 dark:text-slate-200 hover:bg-gray-100"
            >
              <ArrowPathIcon className={`w-4 h-4 ${diagLoading ? "animate-spin" : ""}`} />
              Run Deep Diagnostics
            </button>
          </div>

          {/* 6-Point Diagnostic Health Cards */}
          {Array.isArray(diagnostics?.checks) && (
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3.5">
              {diagnostics.checks.map((chk) => {
                const isOk = chk.status === "ok";
                const isWarn = chk.status === "warn";
                return (
                  <div
                    key={chk.id}
                    className={`p-3.5 rounded-xl border ${
                      isOk
                        ? "border-emerald-200/80 bg-emerald-50/40 dark:border-emerald-500/30 dark:bg-emerald-500/10"
                        : isWarn
                        ? "border-amber-200/80 bg-amber-50/40 dark:border-amber-500/30 dark:bg-amber-500/10"
                        : "border-red-200/80 bg-red-50/40 dark:border-red-500/30 dark:bg-red-500/10"
                    }`}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs font-bold text-gray-900 dark:text-slate-100">
                        {chk.title}
                      </span>
                      {isOk ? (
                        <CheckCircleIcon className="w-4 h-4 text-emerald-600 dark:text-emerald-400 shrink-0" />
                      ) : isWarn ? (
                        <ExclamationTriangleIcon className="w-4 h-4 text-amber-600 dark:text-amber-400 shrink-0" />
                      ) : (
                        <XCircleIcon className="w-4 h-4 text-red-600 dark:text-red-400 shrink-0" />
                      )}
                    </div>
                    <div className="mt-1.5 text-xs font-semibold text-gray-800 dark:text-slate-200">
                      {chk.summary}
                    </div>
                    <div className="mt-1 text-[11px] text-gray-500 dark:text-slate-400 leading-relaxed">
                      {chk.hint}
                    </div>
                  </div>
                );
              })}
            </div>
          )}

          {/* 1-Click Self-Healing Repair Actions */}
          <div className="p-4 rounded-xl bg-slate-50 dark:bg-slate-900/60 border border-slate-200/80 dark:border-slate-700/70">
            <div className="text-xs font-bold uppercase tracking-wider text-gray-700 dark:text-slate-200 mb-3">
              1-Click Self-Healing &amp; Repair Tools
            </div>
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-2.5">
              <button
                type="button"
                onClick={() =>
                  handleRunTroubleshootAction("resync_sequences", "Re-Sync Document Sequences")
                }
                disabled={runningRepair !== null}
                className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 hover:border-blue-400 text-left transition"
              >
                <div className="text-xs font-bold text-blue-600 dark:text-blue-400">
                  Re-Sync Invoice Sequences
                </div>
                <div className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">
                  Aligns SI / SLRET / PRINV counters with table max numbers.
                </div>
              </button>

              <button
                type="button"
                onClick={() =>
                  handleRunTroubleshootAction("force_sync_pulse", "Broadcast Force-Sync Pulse")
                }
                disabled={runningRepair !== null}
                className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 hover:border-indigo-400 text-left transition"
              >
                <div className="text-xs font-bold text-indigo-600 dark:text-indigo-400">
                  Force-Sync All Counters
                </div>
                <div className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">
                  Refreshes stock, held bills &amp; drawers across all PCs.
                </div>
              </button>

              <button
                type="button"
                onClick={() =>
                  handleRunTroubleshootAction("clear_caches", "Clear Server Caches")
                }
                disabled={runningRepair !== null}
                className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 hover:border-emerald-400 text-left transition"
              >
                <div className="text-xs font-bold text-emerald-600 dark:text-emerald-400">
                  Clear Caches &amp; Optimize
                </div>
                <div className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">
                  Flushes route, config, view &amp; schema caches.
                </div>
              </button>

              <button
                type="button"
                onClick={() =>
                  handleRunTroubleshootAction("run_migrations", "Run Pending Migrations")
                }
                disabled={runningRepair !== null}
                className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 hover:border-purple-400 text-left transition"
              >
                <div className="text-xs font-bold text-purple-600 dark:text-purple-400">
                  Run DB Migrations
                </div>
                <div className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">
                  Ensures all database tables &amp; columns are up to date.
                </div>
              </button>

              <button
                type="button"
                onClick={() =>
                  handleRunTroubleshootAction("prune_offline_terminals", "Prune Offline Terminals")
                }
                disabled={runningRepair !== null}
                className="p-3 rounded-xl border border-gray-200 dark:border-slate-700 bg-white dark:bg-slate-800 hover:border-amber-400 text-left transition"
              >
                <div className="text-xs font-bold text-amber-600 dark:text-amber-400">
                  Prune Offline Terminals
                </div>
                <div className="text-[11px] text-gray-500 dark:text-slate-400 mt-0.5">
                  Removes disconnected ghost terminals from the registry.
                </div>
              </button>
            </div>
          </div>

          {/* Live Server Error Logs + CLI Quick Reference */}
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <div className="lg:col-span-2 p-4 rounded-xl bg-slate-900 text-slate-200 border border-slate-700">
              <div className="flex items-center justify-between gap-2 mb-2">
                <div className="flex items-center gap-2 text-xs font-bold text-slate-100">
                  <CommandLineIcon className="w-4 h-4 text-emerald-400" />
                  {serverInfo?.php_version === ".NET 9.0"
                    ? `Live Native Server Log (storage/logs/native-startup.log • ${diagnostics?.log_size_kb ?? 0} KB)`
                    : `Live Server Error Log (storage/logs/laravel.log • ${diagnostics?.log_size_kb ?? 0} KB)`}
                </div>
                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={() => handleRunTroubleshootAction("clear_logs", "Clear Error Log")}
                    className="px-2.5 py-1 rounded-lg text-[11px] font-semibold bg-slate-800 hover:bg-slate-700 text-slate-300"
                  >
                    Clear Log
                  </button>
                </div>
              </div>
              <pre className="text-[11px] font-mono leading-relaxed overflow-x-auto max-h-56 overflow-y-auto p-2.5 rounded-lg bg-slate-950/90 text-slate-300">
                {Array.isArray(diagnostics?.recent_logs) && diagnostics.recent_logs.length > 0
                  ? diagnostics.recent_logs.join("\n")
                  : "✓ No recent errors — server is running cleanly."}
              </pre>
            </div>

            <div className="p-4 rounded-xl bg-slate-50 dark:bg-slate-900/60 border border-slate-200/80 dark:border-slate-700/70 space-y-2.5">
              <div className="text-xs font-bold uppercase tracking-wider text-gray-700 dark:text-slate-200">
                {serverInfo?.php_version === ".NET 9.0"
                  ? "Native Windows Server CLI"
                  : "Linux Server CLI (`karobar-ctl`)"}
              </div>
              <p className="text-[11px] text-gray-500 dark:text-slate-400">
                {serverInfo?.php_version === ".NET 9.0"
                  ? "Run these commands from PowerShell in the Karobar-App-Native folder:"
                  : "You can also run these commands in the Linux terminal at any time:"}
              </p>
              <div className="space-y-1.5 text-[11px] font-mono">
                {(serverInfo?.php_version === ".NET 9.0"
                  ? [
                      "Karobar-Native.exe --server-only",
                      "Karobar-Native.exe --cli install --upgrade-only",
                      "Karobar-Native.exe --cli karobar:export-sqlite",
                      "Karobar-Native.exe --cli karobar:backup",
                      "netsh advfirewall firewall show rule name=\"Karobar-App Native LAN Server (TCP 18960)\"",
                    ]
                  : [
                      "sudo karobar-ctl status",
                      "sudo karobar-ctl terminals",
                      "sudo karobar-ctl restart",
                      "sudo karobar-ctl backup",
                      "sudo karobar-ctl logs",
                    ]
                ).map((cmd) => (
                  <div
                    key={cmd}
                    className="flex items-center justify-between px-2.5 py-1.5 rounded-lg bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700"
                  >
                    <span className="text-gray-800 dark:text-slate-200 truncate">{cmd}</span>
                    <button
                      type="button"
                      onClick={() => handleCopyText(cmd, `Copied: ${cmd}`)}
                      className="text-blue-600 dark:text-blue-400 hover:underline ml-2"
                    >
                      Copy
                    </button>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* 5. Windows Desktop Mode Switcher (Only shown inside Desktop .exe on Windows) */}
      {isElectronDesktop && (
        <div className="rounded-2xl border border-blue-200/80 dark:border-blue-500/30 bg-blue-50/40 dark:bg-slate-800/90 p-5 shadow-sm">
          <div className="flex items-center gap-2.5 pb-3 border-b border-blue-100 dark:border-slate-700">
            <ComputerDesktopIcon className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            <div>
              <h3 className="text-sm font-bold text-gray-900 dark:text-slate-100">
                This Windows PC — Operating Mode (Standalone / Store Server vs. Multi-PC Terminal)
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400">
                Switch this Windows PC between running its own local database (also acts as the LAN Store Server) or connecting as a Counter Terminal to another Store Server PC.
              </p>
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4 mt-4">
            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                PC Operating Mode
              </label>
              <select
                value={desktopNetCfg.mode}
                onChange={(e) => setDesktopNetCfg((p) => ({ ...p, mode: e.target.value }))}
                className="w-full px-3 py-2 rounded-xl border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs font-semibold"
              >
                <option value="standalone">Standalone / Store Server (Local SQLite WAL)</option>
                <option value="terminal">Multi-PC POS Terminal (Connect to Store Server)</option>
              </select>
            </div>

            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Store Server LAN URL
              </label>
              <input
                type="text"
                value={desktopNetCfg.serverUrl}
                onChange={(e) => setDesktopNetCfg((p) => ({ ...p, serverUrl: e.target.value }))}
                placeholder="http://192.168.1.10:18960"
                disabled={desktopNetCfg.mode !== "terminal"}
                className="w-full px-3 py-2 rounded-xl border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs font-mono disabled:opacity-50"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                This Counter / Terminal Name
              </label>
              <input
                type="text"
                value={desktopNetCfg.terminalName}
                onChange={(e) => setDesktopNetCfg((p) => ({ ...p, terminalName: e.target.value }))}
                placeholder="Counter 2 — Pharmacy"
                className="w-full px-3 py-2 rounded-xl border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-gray-700 dark:text-slate-300 mb-1">
                Server Pairing Token (Optional)
              </label>
              <input
                type="text"
                value={desktopNetCfg.pairingToken || ""}
                onChange={(e) => setDesktopNetCfg((p) => ({ ...p, pairingToken: e.target.value }))}
                placeholder="KRB-NET-XXXXXXXXXXXX"
                disabled={desktopNetCfg.mode !== "terminal"}
                className="w-full px-3 py-2 rounded-xl border border-gray-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs font-mono disabled:opacity-50"
              />
            </div>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-3 mt-4">
            <div className="text-xs">
              {connTestResult && (
                <span
                  className={`inline-flex items-center gap-1.5 font-semibold ${
                    connTestResult.ok ? "text-emerald-600 dark:text-emerald-400" : "text-red-600 dark:text-red-400"
                  }`}
                >
                  {connTestResult.ok ? (
                    <>
                      <CheckCircleIcon className="w-4 h-4" />
                      Connected to {connTestResult.storeName} ({connTestResult.dbDriver}, {connTestResult.latencyMs}ms)
                    </>
                  ) : (
                    <>
                      <XCircleIcon className="w-4 h-4" />
                      {connTestResult.error}
                    </>
                  )}
                </span>
              )}
            </div>

            <div className="flex items-center gap-2">
              {desktopNetCfg.mode === "terminal" && (
                <button
                  type="button"
                  onClick={handleTestDesktopServer}
                  disabled={testingConn}
                  className="px-3.5 py-2 rounded-xl text-xs font-semibold border border-blue-300 dark:border-blue-500/40 text-blue-700 dark:text-blue-300 bg-white dark:bg-slate-900 hover:bg-blue-50"
                >
                  {testingConn ? "Testing…" : "Test Server Connection"}
                </button>
              )}
              <button
                type="button"
                onClick={handleSaveDesktopMode}
                disabled={savingDesktopCfg}
                className="px-4 py-2 rounded-xl text-xs font-bold bg-blue-600 text-white hover:bg-blue-700"
              >
                {savingDesktopCfg ? "Applying…" : "Save & Apply PC Mode"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
