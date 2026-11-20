// Gateway API dispatch and the typed fetch helpers built on it.
import { state } from "../stores/state.js";
import { isDemoBuild } from "../utils/demoMode.js";

// Where the portal sends API calls. In the Vite dev server this is the portal's own origin, and its /api proxy forwards
// to the gateway, which sends no CORS headers (#5451); demo builds resolve to same-origin for MSW interception, while
// standard builds and tests default to the gateway's published port.
export function getApiBaseUrl() {
  const configured = import.meta.env?.VITE_API_BASE_URL;
  if (configured) {
    return configured.replace(/\/+$/, "");
  }
  if (import.meta.env.MODE === "demo") {
    return typeof globalThis.location?.origin === "string" &&
      globalThis.location.origin.startsWith("http")
      ? globalThis.location.origin.replace(/\/+$/, "")
      : "";
  }
  if (
    isDemoBuild() &&
    typeof globalThis.location?.origin === "string" &&
    globalThis.location.origin.startsWith("http")
  ) {
    return globalThis.location.origin.replace(/\/+$/, "");
  }
  return "http://localhost:8000";
}

// Auth Fetch Helpers
export async function fetchAssignments(subjectId) {
  return await dispatchApi(`assignments/subject/${subjectId}`);
}

export async function fetchAssignedInstruments(subjectId) {
  return await dispatchApi(`subjects/${subjectId}/instruments`);
}

export async function fetchInstrument(id) {
  return await dispatchApi(`instruments/${id}`);
}

export async function fetchCompliance(subjectId) {
  return await dispatchApi(`subjects/${subjectId}/compliance`);
}

export async function fetchNotifications(subjectId) {
  return await dispatchApi(`subjects/${subjectId}/notifications`);
}

export function isAuthenticatedSession() {
  return !!(state.session.token && !state.session.isOfflineMode);
}

// API Call helper
export async function dispatchApi(endpoint, options = {}) {
  let cleanEndpoint = endpoint;
  if (cleanEndpoint.startsWith("/")) {
    cleanEndpoint = cleanEndpoint.substring(1);
  }

  const apiBaseUrl = getApiBaseUrl();
  let url;
  if (cleanEndpoint.startsWith("api/v1/")) {
    url = `${apiBaseUrl}/${cleanEndpoint}`;
  } else if (cleanEndpoint.startsWith("payments/")) {
    url = `${apiBaseUrl}/api/v1/${cleanEndpoint}`;
  } else {
    url = `${apiBaseUrl}/api/v1/interop/${cleanEndpoint}`;
  }

  const defaultHeaders = {
    "Content-Type": "application/json",
  };

  if (options.change_reason) {
    defaultHeaders["X-Change-Reason"] = options.change_reason;
  } else if (
    options.headers &&
    (options.headers["X-Change-Reason"] || options.headers["x-change-reason"])
  ) {
    defaultHeaders["X-Change-Reason"] =
      options.headers["X-Change-Reason"] || options.headers["x-change-reason"];
  }

  if (state.session.token) {
    defaultHeaders["Authorization"] = `Bearer ${state.session.token}`;
  }

  try {
    const res = await fetch(url, {
      ...options,
      headers: { ...defaultHeaders, ...options.headers },
    });

    if (!res.ok) {
      let errBody = "";
      try {
        const parsed = await res.json();
        if (Array.isArray(parsed.detail)) {
          errBody = parsed.detail
            .map((entry) => {
              if (!entry || typeof entry !== "object") return String(entry);
              const loc = Array.isArray(entry.loc)
                ? entry.loc.filter((p) => p !== "body").join(".")
                : "";
              const msg = entry.msg || "Invalid value";
              return loc ? `${loc}: ${msg}` : msg;
            })
            .join("; ");
        } else if (parsed.detail && typeof parsed.detail === "object") {
          errBody =
            parsed.detail.message ||
            parsed.detail.reason ||
            `Validation error (${res.status})`;
        } else {
          errBody = parsed.detail || parsed.message || "";
        }
      } catch {
        // ignored
      }
      const apiErr = new Error(errBody || `HTTP Error ${res.status}`);
      apiErr.status = res.status;
      apiErr.isValidationError = res.status === 400 || res.status === 422;
      throw apiErr;
    }
    return await res.json();
  } catch (err) {
    console.warn(
      `Gateway API call '${endpoint}' failed (running in sandbox/offline mode):`,
      err.message
    );
    throw err;
  }
}
