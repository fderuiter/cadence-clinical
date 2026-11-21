import { encodeChangeReason } from "./changeReason";
import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./types";
import { getBearerToken } from "./authorization.js";

declare global {
  interface ImportMeta {
    readonly env?: Record<string, string | undefined>;
  }
}

export function getBaseUrl(): string {
  const configured = import.meta.env?.VITE_API_BASE_URL;
  if (configured) return configured;
  const isDemo =
    import.meta.env?.MODE === "demo" ||
    import.meta.env?.VITE_DEMO_MODE === "true";
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

async function dispatchOpenApiFetch(
  input: RequestInfo | URL,
  init?: RequestInit,
  initialBaseUrl: string = INITIAL_BASE_URL
): Promise<Response> {
  const currentBase = getBaseUrl().replace(/\/+$/, "");
  const initialBase = initialBaseUrl.replace(/\/+$/, "");
  if (
    currentBase !== initialBase &&
    input instanceof Request &&
    input.url.startsWith(`${initialBase}/`)
  ) {
    const rewrittenUrl = `${currentBase}${input.url.slice(initialBase.length)}`;
    const hasBody = input.method !== "GET" && input.method !== "HEAD";
    const body = hasBody ? await input.clone().arrayBuffer() : undefined;
    const rewritten = new Request(rewrittenUrl, {
      method: input.method,
      headers: input.headers,
      body: body && body.byteLength > 0 ? body : undefined,
      credentials: input.credentials,
      mode: input.mode === "navigate" ? undefined : input.mode,
      cache: input.cache,
      redirect: input.redirect,
      referrer: input.referrer,
      referrerPolicy: input.referrerPolicy,
      integrity: input.integrity,
      keepalive: input.keepalive,
      signal: input.signal,
    });
    return globalThis.fetch(rewritten, init);
  }
  return globalThis.fetch(input, init);
}

function createDynamicBaseFetch(initialBaseUrl: string) {
  return (input: RequestInfo | URL, init?: RequestInit): Promise<Response> =>
    dispatchOpenApiFetch(input, init, initialBaseUrl);
}

export const openApiClient = createClient<paths>({
  baseUrl: INITIAL_BASE_URL,
  fetch: createDynamicBaseFetch(INITIAL_BASE_URL),
});

openApiClient.use(getAuthMiddleware());

export function createOpenApiClient<T extends {} = paths>(baseUrl?: string) {
  const resolvedBaseUrl = baseUrl ?? getBaseUrl();
  // Resolve fetch per call, as openApiClient does, so tests, Vite circular module init, and the MSW worker see the current global/base URL.
  const client = createClient<T>({
    baseUrl: resolvedBaseUrl,
    fetch:
      baseUrl === undefined
        ? createDynamicBaseFetch(resolvedBaseUrl)
        : (...args) => globalThis.fetch(...args),
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
