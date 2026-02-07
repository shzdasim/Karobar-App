import axios from "axios";

export function initAxiosAuth() {
  axios.defaults.headers.common["X-Requested-With"] = "XMLHttpRequest";

  const t = localStorage.getItem("token");
  if (t) {
    axios.defaults.headers.common["Authorization"] = `Bearer ${t}`;
  } else {
    delete axios.defaults.headers.common["Authorization"];
  }

  axios.interceptors.response.use(
    (r) => r,
    (err) => {
      const status = err?.response?.status;
      const url = err?.config?.url || "";

      // Only handle license errors from actual license-protected endpoints
      // Don't redirect for /license/status - let the LicenseContext handle it
      const isLicenseApi = url.includes("/license/");
      const isLicenseStatusEndpoint = url === "/api/license/status";
      const isAuth = url.includes("/login") || url.includes("/user") || url.includes("/me");

      // Only redirect to activate for 402 from protected API endpoints (not status check)
      if (status === 402 && !isLicenseApi && !isLicenseStatusEndpoint && !isAuth) {
        // stop console spam & bounce to activate
        window.location.assign("/activate");
        return; // don't rethrow
      }

      // Handle token expiry from our custom middleware
      if (status === 401 && err?.response?.data?.expired) {
        localStorage.removeItem("token");
        localStorage.removeItem("user");
        localStorage.removeItem("token_expires_at");
        localStorage.removeItem("remembered_email");
        window.location.href = "/";
        return;
      }

      // Optional: handle 401 globally for auth failures
      if (status === 401 && !isAuth && !isLicenseStatusEndpoint) {
        localStorage.removeItem("token");
        localStorage.removeItem("user");
        localStorage.removeItem("token_expires_at");
        localStorage.removeItem("remembered_email");
        // Don't redirect automatically - let components handle it
      }

      return Promise.reject(err);
    }
  );
}
