const LAST_VISITED_STORAGE_KEY = "karobar_last_visited_path";

const EXCLUDED_PATHNAMES = new Set([
  "/",
  "/login",
  "/activate",
  "/forgot-password",
]);

/**
 * Returns true if the given path is a valid, authenticated workspace route
 * that can be saved and restored after login.
 */
export function isTrackableAppPath(path) {
  if (!path || typeof path !== "string") return false;
  const trimmed = path.trim();
  if (!trimmed.startsWith("/") || trimmed.startsWith("//")) return false;

  const cleanPathname =
    trimmed.split("?")[0].split("#")[0].replace(/\/+$/, "") || "/";

  if (EXCLUDED_PATHNAMES.has(cleanPathname)) return false;
  if (cleanPathname.startsWith("/reset-password")) return false;

  return true;
}

/**
 * Persists the user's current workspace path so login can return them
 * directly to their last visited page.
 */
export function saveLastVisitedPath(pathOrLocation) {
  try {
    let fullPath = "";
    if (typeof pathOrLocation === "string") {
      fullPath = pathOrLocation.trim();
    } else if (pathOrLocation && typeof pathOrLocation === "object") {
      const pathname = pathOrLocation.pathname || "";
      const search = pathOrLocation.search || "";
      const hash = pathOrLocation.hash || "";
      fullPath = `${pathname}${search}${hash}`;
    }

    if (!isTrackableAppPath(fullPath)) return;
    localStorage.setItem(LAST_VISITED_STORAGE_KEY, fullPath);
  } catch {
    /* ignore storage errors */
  }
}

/**
 * Resolves the post-login destination: prefers an explicit valid `preferredPath`
 * (such as `location.state?.from`), then the stored last visited page,
 * and falls back to `/dashboard`.
 */
export function getLastVisitedPath(preferredPath = null) {
  if (typeof preferredPath === "string" && isTrackableAppPath(preferredPath)) {
    return preferredPath.trim();
  }
  if (
    preferredPath &&
    typeof preferredPath === "object" &&
    typeof preferredPath.pathname === "string"
  ) {
    const candidate = `${preferredPath.pathname}${preferredPath.search || ""}${
      preferredPath.hash || ""
    }`;
    if (isTrackableAppPath(candidate)) {
      return candidate;
    }
  }

  try {
    const stored = localStorage.getItem(LAST_VISITED_STORAGE_KEY);
    if (stored && isTrackableAppPath(stored)) {
      return stored;
    }
  } catch {
    /* ignore storage errors */
  }

  return "/dashboard";
}

