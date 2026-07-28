import { useAuthStore } from "../stores/auth";

// Helper to get the current API base URL dynamically
export const getBaseUrl = () => {
  return import.meta.env?.VITE_API_BASE_URL || "http://localhost:8000";
};

/**
 * Custom error class representing API network, status, or business failures.
 */
export class ApiError extends Error {
  constructor(message, status = null, statusText = null, data = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.statusText = statusText;
    this.data = data;
  }
}

/**
 * Generic request helper.
 * Automatically resolves the bearer token from the Pinia auth store if present.
 */
async function request(path, options = {}) {
  let token = null;
  try {
    const authStore = useAuthStore();
    token = authStore?.token || authStore?.accessToken;
  } catch (err) {
    // Pinia not active or initialized, ignore or log
  }

  const { method = "GET", headers = {}, body, changeReason, ...customOptions } = options;

  const requestHeaders = {
    "Content-Type": "application/json",
    ...headers,
  };

  if (token) {
    requestHeaders["Authorization"] = `Bearer ${token}`;
  }

  // Caller can supply a change reason for mutations, passed as X-Change-Reason
  const upperMethod = method.toUpperCase();
  const isMutation = ["POST", "PUT", "DELETE", "PATCH"].includes(upperMethod);
  const resolvedChangeReason = changeReason || headers["X-Change-Reason"] || headers["x-change-reason"];

  if (isMutation && resolvedChangeReason) {
    requestHeaders["X-Change-Reason"] = resolvedChangeReason;
  }

  // Construct URL cleanly reading base URL dynamically
  const baseUrl = getBaseUrl();
  const cleanPath = path.startsWith("/") ? path : `/${path}`;
  const url = `${baseUrl}${cleanPath}`;

  const fetchOptions = {
    method: upperMethod,
    headers: requestHeaders,
    ...customOptions,
  };

  if (body) {
    fetchOptions.body = typeof body === "string" ? body : JSON.stringify(body);
  }

  try {
    const response = await fetch(url, fetchOptions);
    if (!response.ok) {
      let data = null;
      try {
        data = await response.json();
      } catch (_) {
        // Not JSON
      }
      throw new ApiError(
        data?.detail || data?.message || `Request failed with status ${response.status}`,
        response.status,
        response.statusText,
        data
      );
    }

    if (response.status === 204) {
      return null;
    }
    return await response.json();
  } catch (error) {
    if (error instanceof ApiError) {
      throw error;
    }
    throw new ApiError(error.message || "Network or unknown error occurred");
  }
}

export const apiClient = {
  get(path, options = {}) {
    return request(path, { ...options, method: "GET" });
  },
  post(path, body, options = {}) {
    return request(path, { ...options, method: "POST", body });
  },
  put(path, body, options = {}) {
    return request(path, { ...options, method: "PUT", body });
  },
  patch(path, body, options = {}) {
    return request(path, { ...options, method: "PATCH", body });
  },
  delete(path, options = {}) {
    return request(path, { ...options, method: "DELETE" });
  },
};
