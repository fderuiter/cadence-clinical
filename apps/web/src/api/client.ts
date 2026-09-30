import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./types";
import { useAuthStore } from "../stores/auth";

export const getBaseUrl = (): string => {
  return (
    (import.meta as unknown as { env?: { VITE_API_BASE_URL?: string } }).env
      ?.VITE_API_BASE_URL || "http://localhost:8000"
  );
};

export class ApiError extends Error {
  status: number | null;
  statusText: string | null;
  data: unknown;

  constructor(
    message: string,
    status: number | null = null,
    statusText: string | null = null,
    data: unknown = null
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.statusText = statusText;
    this.data = data;
  }
}

const authMiddleware: Middleware = {
  async onRequest({ request, options }) {
    try {
      const authStore = useAuthStore();
      const token = authStore?.token || authStore?.accessToken;
      if (token && !request.headers.has("Authorization")) {
        request.headers.set("Authorization", `Bearer ${token}`);
      }
    } catch {
      // Pinia store not active or initialized
    }

    // openapi-fetch copies custom init keys (like `changeReason`) onto the Request object; the
    // middleware `options` only holds the client's merged options (baseUrl, fetch, serializers).
    const changeReason = ((request as unknown as Record<string, unknown>)
      .changeReason ??
      (options as unknown as Record<string, unknown>)?.changeReason) as
      string | undefined;
    if (changeReason && !request.headers.has("X-Change-Reason")) {
      request.headers.set("X-Change-Reason", changeReason);
    }

    return request;
  },
};

export const openApiClient = createClient<paths>({
  baseUrl: getBaseUrl(),
  fetch: (...args) => globalThis.fetch(...args),
});

openApiClient.use(authMiddleware);

export function createOpenApiClient<T extends {} = paths>(
  baseUrl: string = getBaseUrl()
) {
  // Resolve fetch per call, as openApiClient does, so tests and the MSW worker see the current global.
  const client = createClient<T>({
    baseUrl,
    fetch: (...args) => globalThis.fetch(...args),
  });
  client.use(authMiddleware);
  return client;
}

export async function unwrapResponse<T>(
  promise: Promise<{ data?: T; error?: unknown; response: Response }>
): Promise<T> {
  const { data, error, response } = await promise;
  if (error || !response.ok) {
    const errData = error || null;
    const detail =
      (errData as Record<string, unknown>)?.detail ||
      (errData as Record<string, unknown>)?.message ||
      `Request failed with status ${response?.status ?? 500}`;
    throw new ApiError(
      typeof detail === "string" ? detail : JSON.stringify(detail),
      response?.status ?? null,
      response?.statusText ?? null,
      errData
    );
  }
  return data as T;
}

export default openApiClient;
