import { encodeChangeReason } from "./changeReason";
import {
  getApiBaseUrl,
  getBearerToken,
  notifySubscriptionExpiredIfNeeded,
  refreshBearerTokenOnUnauthorized,
} from "./authorization.js";

// Helper to get the current API base URL dynamically
export const getBaseUrl = getApiBaseUrl;

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

const pendingRequests = new Map();

/**
 * Generic request helper.
 * Attaches the signed-in user's Keycloak access token (refreshed first when it
 * is about to expire) as `Authorization: Bearer <token>`.
 */
async function request(path, options = {}) {
  const token = await getBearerToken();

  const {
    method = "GET",
    headers = {},
    body,
    changeReason,
    ...customOptions
  } = options;

  const upperMethod = method.toUpperCase();
  const requestKey = `${upperMethod}:${path}`;

  if (pendingRequests.has(requestKey)) {
    try {
      pendingRequests.get(requestKey).abort();
    } catch {
      // ignore
    }
  }

  const controller = new AbortController();
  pendingRequests.set(requestKey, controller);

  const isFormData =
    typeof FormData !== "undefined" && body instanceof FormData;

  const requestHeaders = {
    ...(isFormData ? {} : { "Content-Type": "application/json" }),
    ...headers,
  };

  if (token) {
    requestHeaders["Authorization"] = `Bearer ${token}`;
  }

  // Caller can supply a change reason for mutations, passed as X-Change-Reason
  const isMutation = ["POST", "PUT", "DELETE", "PATCH"].includes(upperMethod);
  const resolvedChangeReason =
    changeReason || headers["X-Change-Reason"] || headers["x-change-reason"];

  if (isMutation && resolvedChangeReason) {
    requestHeaders["X-Change-Reason"] =
      encodeChangeReason(resolvedChangeReason);
  }

  // Construct URL cleanly reading base URL dynamically
  const baseUrl = getBaseUrl();
  const cleanPath = path.startsWith("/") ? path : `/${path}`;
  const url = `${baseUrl}${cleanPath}`;

  const fetchOptions = {
    method: upperMethod,
    headers: requestHeaders,
    signal: controller.signal,
    ...customOptions,
  };

  if (body) {
    if (isFormData || typeof body === "string") {
      fetchOptions.body = body;
    } else {
      fetchOptions.body = JSON.stringify(body);
    }
  }

  try {
    let response = await fetch(url, fetchOptions);
    if (response.status === 401 && !options._retriedAfterRefresh) {
      const errBody = await response
        .clone()
        .json()
        .catch(() => null);
      const freshToken = await refreshBearerTokenOnUnauthorized(url, errBody);
      if (freshToken) {
        response = await fetch(url, {
          ...fetchOptions,
          headers: {
            ...requestHeaders,
            Authorization: `Bearer ${freshToken}`,
          },
        });
      }
    }
    if (!response.ok) {
      let data = null;
      try {
        data = await response.json();
      } catch {
        // Not JSON
      }
      const expiredGuidance = notifySubscriptionExpiredIfNeeded(
        response.status,
        data
      );
      throw new ApiError(
        expiredGuidance ||
          data?.detail ||
          data?.message ||
          `Request failed with status ${response.status}`,
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
    if (
      error.name === "AbortError" ||
      error.message?.toLowerCase().includes("abort") ||
      error.statusText === "abort"
    ) {
      throw error;
    }
    throw new ApiError(error.message || "Network or unknown error occurred");
  } finally {
    if (pendingRequests.get(requestKey) === controller) {
      pendingRequests.delete(requestKey);
    }
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

export const api = apiClient;
