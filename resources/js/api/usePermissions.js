import { useCallback, useEffect, useMemo, useState } from "react";
import axios from "axios";

export function usePermissions() {
  const [loading, setLoading] = useState(true);
  const [roles, setRoles] = useState(new Set());
  const [perms, setPerms] = useState(new Set());

  useEffect(() => {
    (async () => {
      try {
        const { data } = await axios.get("/api/me");
        setRoles(new Set((data?.roles || []).filter(Boolean)));
        setPerms(new Set((data?.permissions || []).filter(Boolean)));
      } catch {
        setRoles(new Set());
        setPerms(new Set());
      } finally {
        setLoading(false);
      }
    })();
  }, []);

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
