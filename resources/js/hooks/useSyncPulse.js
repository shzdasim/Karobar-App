import { useEffect, useRef, useState } from "react";
import axios from "axios";
import { clearApiCache } from "../context/axiosSetup.js";

/**
 * Polls `/api/sync/pulse` every `intervalMs` (default 4000ms) to synchronize
 * inventory stock, held bills, cash drawer state, and active terminal counts
 * across all connected Multi-PC LAN terminals.
 */
export function useSyncPulse({ enabled = true, intervalMs = 4000 } = {}) {
  const [pulseState, setPulseState] = useState({
    online: true,
    onlineTerminalsCount: 1,
    openHeldBillsCount: 0,
    currentTerminal: null,
    lastSyncAt: null,
  });

  const lastVersionsRef = useRef({
    stock_version: null,
    drafts_version: null,
    drawer_version: null,
    terminals_version: null,
    settings_version: null,
  });

  useEffect(() => {
    if (!enabled) return undefined;

    let mounted = true;
    let timer = null;

    // Sync desktop network config into localStorage for axios header injection
    if (window.karobarDesktop?.getNetworkConfig) {
      window.karobarDesktop
        .getNetworkConfig()
        .then((cfg) => {
          if (cfg?.terminalId) {
            localStorage.setItem("karobar_terminal_uid", cfg.terminalId);
          }
          if (cfg?.terminalName) {
            localStorage.setItem("karobar_terminal_name", cfg.terminalName);
          }
          if (cfg?.mode) {
            localStorage.setItem("karobar_terminal_mode", cfg.mode);
          }
        })
        .catch(() => {});
    }

    const pollPulse = async () => {
      try {
        const res = await axios.get("/api/sync/pulse", {
          headers: { "X-Skip-Cache": "1" },
        });
        if (!mounted) return;

        const data = res?.data || {};
        const versions = data.versions || {};
        const prev = lastVersionsRef.current;

        if (prev.stock_version !== null && versions.stock_version !== prev.stock_version) {
          clearApiCache("/api/products");
          clearApiCache("/api/batches");
          window.dispatchEvent(new CustomEvent("karobar:inventory-changed", { detail: { remote: true } }));
        }

        if (prev.drafts_version !== null && versions.drafts_version !== prev.drafts_version) {
          clearApiCache("/api/invoice-drafts");
          window.dispatchEvent(new CustomEvent("karobar:drafts-changed", { detail: { remote: true } }));
        }

        if (prev.drawer_version !== null && versions.drawer_version !== prev.drawer_version) {
          clearApiCache("/api/cash-drawer");
          clearApiCache("/api/accounts");
          window.dispatchEvent(new CustomEvent("cash-drawer-changed"));
          window.dispatchEvent(new CustomEvent("accounts-changed"));
        }

        if (prev.terminals_version !== null && versions.terminals_version !== prev.terminals_version) {
          window.dispatchEvent(new CustomEvent("karobar:terminals-changed"));
        }

        lastVersionsRef.current = {
          stock_version: versions.stock_version ?? prev.stock_version,
          drafts_version: versions.drafts_version ?? prev.drafts_version,
          drawer_version: versions.drawer_version ?? prev.drawer_version,
          terminals_version: versions.terminals_version ?? prev.terminals_version,
          settings_version: versions.settings_version ?? prev.settings_version,
        };

        setPulseState({
          online: true,
          onlineTerminalsCount: Number(data.online_terminals_count || 1),
          openHeldBillsCount: Number(data.open_held_bills_count || 0),
          currentTerminal: data.terminal || null,
          lastSyncAt: data.server_time || new Date().toISOString(),
        });
      } catch {
        if (!mounted) return;
        setPulseState((s) => ({ ...s, online: false }));
      }
    };

    pollPulse();
    timer = window.setInterval(pollPulse, intervalMs);

    return () => {
      mounted = false;
      if (timer) window.clearInterval(timer);
    };
  }, [enabled, intervalMs]);

  return pulseState;
}
