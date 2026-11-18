import { useEffect, useRef, useState } from "react";
import axios from "axios";
import { clearApiCache } from "../context/axiosSetup.js";

const PULSE_CHANNEL_NAME = "karobar_sync_pulse_bus";
const PULSE_LEADER_KEY = "karobar_sync_pulse_leader";
const LEADER_TTL_MS = 10_000;
const STANDALONE_INTERVAL_MS = 20_000;

function getWindowId() {
  if (typeof window === "undefined") return "ssr";
  if (!window.__karobarWindowId) {
    window.__karobarWindowId = `win_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 8)}`;
  }
  return window.__karobarWindowId;
}

function tryClaimLeadership(winId) {
  try {
    const now = Date.now();
    const raw = localStorage.getItem(PULSE_LEADER_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      if (parsed?.id && parsed.id !== winId && now - Number(parsed.ts || 0) < LEADER_TTL_MS) {
        return false;
      }
    }
    localStorage.setItem(PULSE_LEADER_KEY, JSON.stringify({ id: winId, ts: now }));
    return true;
  } catch {
    return true;
  }
}

function refreshLeadership(winId) {
  try {
    localStorage.setItem(PULSE_LEADER_KEY, JSON.stringify({ id: winId, ts: Date.now() }));
  } catch {
    // ignore storage errors
  }
}

/**
 * Polls `/api/sync/pulse` via single-leader election across all open windows on the same host
 * so N open windows only make 1 pulse request instead of N concurrent requests.
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

  const onlineTerminalsRef = useRef(1);

  useEffect(() => {
    if (!enabled) return undefined;

    let mounted = true;
    let timer = null;
    let channel = null;
    const winId = getWindowId();

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

    const applyPulseData = (data) => {
      if (!mounted || !data) return;
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

      const nextOnlineCount = Number(data.online_terminals_count || 1);
      onlineTerminalsRef.current = nextOnlineCount;

      setPulseState({
        online: true,
        onlineTerminalsCount: nextOnlineCount,
        openHeldBillsCount: Number(data.open_held_bills_count || 0),
        currentTerminal: data.terminal || null,
        lastSyncAt: data.server_time || new Date().toISOString(),
      });
    };

    try {
      if (typeof BroadcastChannel !== "undefined") {
        channel = new BroadcastChannel(PULSE_CHANNEL_NAME);
        channel.onmessage = (event) => {
          if (event?.data?.type === "pulse" && event.data.payload) {
            applyPulseData(event.data.payload);
          } else if (event?.data?.type === "offline") {
            if (mounted) setPulseState((s) => ({ ...s, online: false }));
          }
        };
      }
    } catch {
      channel = null;
    }

    const getEffectiveInterval = () => {
      try {
        const mode = localStorage.getItem("karobar_terminal_mode") || "standalone";
        if (mode === "standalone" && onlineTerminalsRef.current <= 1) {
          return Math.max(intervalMs, STANDALONE_INTERVAL_MS);
        }
      } catch {
        // ignore
      }
      return intervalMs;
    };

    const scheduleNext = () => {
      if (!mounted) return;
      if (timer) window.clearTimeout(timer);
      timer = window.setTimeout(pollPulse, getEffectiveInterval());
    };

    const pollPulse = async () => {
      if (!mounted) return;

      // Skip network polling while this window is hidden/minimized; another visible window or visibilitychange will handle it
      if (typeof document !== "undefined" && document.hidden) {
        scheduleNext();
        return;
      }

      // Only the elected leader window polls the backend and broadcasts to all follower windows
      if (!tryClaimLeadership(winId)) {
        scheduleNext();
        return;
      }

      try {
        refreshLeadership(winId);
        const res = await axios.get("/api/sync/pulse", {
          headers: { "X-Skip-Cache": "1" },
        });
        if (!mounted) return;
        const data = res?.data || {};
        applyPulseData(data);
        try {
          channel?.postMessage({ type: "pulse", payload: data });
        } catch {
          // ignore
        }
      } catch {
        if (!mounted) return;
        setPulseState((s) => ({ ...s, online: false }));
        try {
          channel?.postMessage({ type: "offline" });
        } catch {
          // ignore
        }
      } finally {
        scheduleNext();
      }
    };

    const onVisibilityChange = () => {
      if (typeof document !== "undefined" && !document.hidden) {
        pollPulse();
      }
    };

    document.addEventListener("visibilitychange", onVisibilityChange);
    pollPulse();

    return () => {
      mounted = false;
      if (timer) window.clearTimeout(timer);
      document.removeEventListener("visibilitychange", onVisibilityChange);
      try {
        const raw = localStorage.getItem(PULSE_LEADER_KEY);
        if (raw) {
          const parsed = JSON.parse(raw);
          if (parsed?.id === winId) {
            localStorage.removeItem(PULSE_LEADER_KEY);
          }
        }
      } catch {
        // ignore
      }
      try {
        channel?.close();
      } catch {
        // ignore
      }
    };
  }, [enabled, intervalMs]);

  return pulseState;
}
