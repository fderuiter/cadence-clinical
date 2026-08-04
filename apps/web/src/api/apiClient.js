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

let webHttpClient = null;

async function getClient() {
  if (!webHttpClient) {
    const { HttpClient } = await import("ui");
    webHttpClient = new HttpClient({
      baseUrl: getBaseUrl,
      authResolver: () => {
        try {
          const authStore = useAuthStore();
          return authStore?.token || authStore?.accessToken || null;
        } catch {
          return null;
        }
      },
    });
  }
  return webHttpClient;
}

/**
 * Generic request helper wrapping the centralized HttpClient.
 */
async function request(path, options = {}) {
  try {
    const client = await getClient();
    return await client.request(path, options);
  } catch (error) {
    if (error.name === "HttpClientError" || error.status !== undefined) {
      throw new ApiError(error.message, error.status, error.statusText, error.data);
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
