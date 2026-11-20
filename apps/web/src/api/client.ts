import { encodeChangeReason } from "./changeReason";
import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./types";
import {
  getBearerToken,
  refreshBearerTokenOnUnauthorized,
} from "./authorization.js";

declare global {
  interface ImportMeta {
    readonly env: Record<string, string | undefined>;
  }
}

export function getBaseUrl(): string {
  const configured = import.meta.env?.VITE_API_BASE_URL;
  if (configured) return configured;
  const isDemo =
    import.meta.env.MODE === "demo" ||
    import.meta.env.VITE_DEMO_MODE === "true";
  if (
    isDemo &&
    typeof globalThis.location?.origin === "string" &&
    globalThis.location.origin.startsWith("http")
  ) {
    return globalThis.location.origin;
  }
  return "http://localhost:8000";
}

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

export function getAuthMiddleware(): Middleware {
  return {
    async onRequest({ request, options }) {
      if (!request.headers.has("Authorization")) {
        const token = await getBearerToken();
        if (token) {
          request.headers.set("Authorization", `Bearer ${token}`);
        }
      }

      // openapi-fetch copies custom init keys (like `changeReason`) onto the Request object; the
      // middleware `options` only holds the client's merged options (baseUrl, fetch, serializers).
      const changeReason = ((request as unknown as Record<string, unknown>)
        .changeReason ??
        (options as unknown as Record<string, unknown>)?.changeReason) as
        string | undefined;
      if (changeReason && !request.headers.has("X-Change-Reason")) {
        request.headers.set(
          "X-Change-Reason",
          encodeChangeReason(changeReason)
        );
      }

      return request;
    },
  };
}

const INITIAL_BASE_URL = getBaseUrl();

async function executeWithSingleFlightRetry(
  input: RequestInfo | URL,
  init?: RequestInit
): Promise<Response> {
  const url = input instanceof Request ? input.url : String(input);
  const retryClone = input instanceof Request ? input.clone() : null;
  const response = await globalThis.fetch(input, init);
  if (response.status !== 401) {
    return response;
  }
  const errBody = await response
    .clone()
    .json()
    .catch(() => null);
  const freshToken = await refreshBearerTokenOnUnauthorized(url, errBody);
  if (!freshToken) {
    return response;
  }
  if (retryClone) {
    const retryHeaders = new Headers(retryClone.headers);
    if (init?.headers) {
      new Headers(init.headers).forEach((value, key) => {
        retryHeaders.set(key, value);
      });
    }
    retryHeaders.set("Authorization", `Bearer ${freshToken}`);
    return globalThis.fetch(
      new Request(retryClone, { headers: retryHeaders }),
      init
    );
  }
  const retryHeaders = new Headers(init?.headers);
  retryHeaders.set("Authorization", `Bearer ${freshToken}`);
  return globalThis.fetch(input, { ...init, headers: retryHeaders });
}

function dispatchOpenApiFetch(input: RequestInfo | URL, init?: RequestInit) {
  const currentBase = getBaseUrl().replace(/\/+$/, "");
  const initialBase = INITIAL_BASE_URL.replace(/\/+$/, "");
  if (
    currentBase !== initialBase &&
    input instanceof Request &&
    input.url.startsWith(`${initialBase}/`)
  ) {
    const rewrittenUrl = `${currentBase}${input.url.slice(initialBase.length)}`;
    return executeWithSingleFlightRetry(new Request(rewrittenUrl, input), init);
  }
  return executeWithSingleFlightRetry(input, init);
}

export const openApiClient = createClient<paths>({
  baseUrl: INITIAL_BASE_URL,
  fetch: dispatchOpenApiFetch,
});

openApiClient.use(getAuthMiddleware());

export function createOpenApiClient<T extends {} = paths>(
  baseUrl: string = getBaseUrl()
) {
  // Resolve fetch per call, as openApiClient does, so tests and the MSW worker see the current global.
  const client = createClient<T>({
    baseUrl,
    fetch: (input: RequestInfo | URL, init?: RequestInit) =>
      executeWithSingleFlightRetry(input, init),
  });
  client.use(getAuthMiddleware());
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
