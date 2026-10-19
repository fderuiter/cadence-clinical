// Gateway API dispatch and the typed fetch helpers built on it.
import { state } from "../stores/state.js";

// Where the portal sends API calls. In the Vite dev server this is the portal's own origin, and its /api proxy forwards
// to the gateway, which sends no CORS headers (#5451); builds and tests default to the gateway's published port.
const API_BASE_URL = (
  import.meta.env?.VITE_API_BASE_URL || "http://localhost:8000"
).replace(/\/+$/, "");

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

  let url;
  if (cleanEndpoint.startsWith("api/v1/")) {
    url = `${API_BASE_URL}/${cleanEndpoint}`;
  } else if (cleanEndpoint.startsWith("payments/")) {
    url = `${API_BASE_URL}/api/v1/${cleanEndpoint}`;
  } else {
    url = `${API_BASE_URL}/api/v1/interop/${cleanEndpoint}`;
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
        errBody = parsed.detail || parsed.message || "";
      } catch {
        // ignored
      }
      throw new Error(errBody || `HTTP Error ${res.status}`);
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
