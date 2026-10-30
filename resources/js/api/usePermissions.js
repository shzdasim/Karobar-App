import { useCallback, useEffect, useState } from "react";
import axios from "axios";

const PERMS_STORAGE_KEY = "karobar_perms_cache_v1";

let cachedToken = null;
let cachedRoles = null;
let cachedPerms = null;
let inflightMePromise = null;
const subscribers = new Set();

function hydrateFromSession() {
  if ( typeof window === "undefined" ) return;
  const currentToken = localStorage.getItem("token") || null;
  if (!currentToken) {
    cachedToken = null;
    cachedRoles = null;
    cachedPerms = null;
    return;
  }
  if (cachedToken === currentToken && cachedRoles && cachedPerms) {
    return;
  }
  try {
    const raw = sessionStorage.getItem(PERMS_STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      if (parsed && parsed.token === currentToken && Array.isArray(parsed.roles) && Array.isArray(parsed.perms)) {
        cachedToken = currentToken;
        cachedRoles = new Set(parsed.roles);
        cachedPerms = new Set(parsed.perms);
      }
    }
  } catch {
    /* ignore storage errors */
  }
}

function notifySubscribers() {
  subscribers.forEach((cb) => {
    try {
      cb();
    } catch {
      /* ignore */
    }
  });
}

export function invalidatePermissionsCache() {
  cachedToken = null;
  cachedRoles = null;
  cachedPerms = null;
  inflightMePromise = null;
  try {
    sessionStorage.removeItem(PERMS_STORAGE_KEY);
  } catch {
    /* ignore */
  }
  notifySubscribers();
}

function fetchPermissionsShared(force = false) {
  const currentToken = typeof window !== "undefined" ? localStorage.getItem("token") : null;
  if (!currentToken) {
    cachedToken = null;
    cachedRoles = new Set();
    cachedPerms = new Set();
    notifySubscribers();
    return Promise.resolve({ roles: cachedRoles, perms: cachedPerms });
  }

  if (!force && cachedToken === currentToken && cachedRoles && cachedPerms) {
    return Promise.resolve({ roles: cachedRoles, perms: cachedPerms });
  }

  if (inflightMePromise) {
    return inflightMePromise;
  }

  inflightMePromise = axios
    .get("/api/me")
    .then(({ data }) => {
      const rArr = (data?.roles || []).filter(Boolean);
      const pArr = (data?.permissions || []).filter(Boolean);
      cachedToken = currentToken;
      cachedRoles = new Set(rArr);
      cachedPerms = new Set(pArr);
      try {
        sessionStorage.setItem(
          PERMS_STORAGE_KEY,
          JSON.stringify({ token: currentToken, roles: rArr, perms: pArr })
        );
      } catch {
        /* ignore */
      }
      inflightMePromise = null;
      notifySubscribers();
      return { roles: cachedRoles, perms: cachedPerms };
    })
    .catch(() => {
      inflightMePromise = null;
      if (!cachedRoles) cachedRoles = new Set();
      if (!cachedPerms) cachedPerms = new Set();
      notifySubscribers();
      return { roles: cachedRoles, perms: cachedPerms };
    });

  return inflightMePromise;
}

export function usePermissions() {
  hydrateFromSession();
  const currentToken = typeof window !== "undefined" ? localStorage.getItem("token") : null;
  const hasCached = Boolean(currentToken && cachedToken === currentToken && cachedRoles && cachedPerms);

  const [state, setState] = useState(() => ({
    loading: Boolean(currentToken && !hasCached),
    roles: hasCached ? cachedRoles : new Set(),
    perms: hasCached ? cachedPerms : new Set(),
  }));

  useEffect(() => {
    let mounted = true;
    const syncState = () => {
      if (!mounted) return;
      const tok = localStorage.getItem("token");
      const ready = Boolean(tok && cachedToken === tok && cachedRoles && cachedPerms);
      setState({
        loading: Boolean(tok && !ready && inflightMePromise),
        roles: cachedRoles || new Set(),
        perms: cachedPerms || new Set(),
      });
    };

    subscribers.add(syncState);

    const tok = localStorage.getItem("token");
    if (!tok) {
      setState({ loading: false, roles: new Set(), perms: new Set() });
    } else if (cachedToken === tok && cachedRoles && cachedPerms) {
      setState({ loading: false, roles: cachedRoles, perms: cachedPerms });
    } else {
      fetchPermissionsShared();
    }

    return () => {
      mounted = false;
      subscribers.delete(syncState);
    };
  }, []);

  const { loading, roles, perms } = state;

  const has = useCallback(
    (ability) =>
      roles.has("Admin") ||
      perms.has(ability) ||
      (typeof ability === "string" && ability.startsWith("user.") && perms.has("user.manage")),
    [roles, perms]
  );

  const canFor = useCallback(
    (mod) => ({
      view:        has(`${mod}.view`),
      create:      has(`${mod}.create`),
      update:      has(`${mod}.update`),
      edit:        has(`${mod}.edit`),
      delete:      has(`${mod}.delete`),
      export:      has(`${mod}.export`),
      import:      has(`${mod}.import`),
      restore:     has(`${mod}.restore`),
      upload:      has(`${mod}.upload`),
      finalize:    has(`${mod}.finalize`),
      dispose:     has(`${mod}.dispose`),
      adjust:      has(`${mod}.adjust`),
      transfer:    has(`${mod}.transfer`),
      generate:    has(`${mod}.generate`),
      cancel:      has(`${mod}.cancel`),
      open:        has(`${mod}.open`),
      close:       has(`${mod}.close`),
      transaction: has(`${mod}.transaction`),
      assignRoles: has(`${mod}.assign.roles`),
      assignPerms: has(`${mod}.assign.permissions`),
      syncPerms:   has(`${mod}.sync.permissions`),
    }),
    [has]
  );

  return { loading, roles, perms, has, canFor };
}

export function Guard({ when, children, fallback = null }) {
  return when ? children : fallback;
}
