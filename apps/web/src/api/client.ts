import { encodeChangeReason } from "./changeReason";
import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./types";
import { getBearerToken } from "./authorization.js";

declare global {
  interface ImportMeta {
    readonly env: Record<string, string | undefined>;
  }
}

export function getBaseUrl(): string {
  const configured = import.meta.env?.VITE_API_BASE_URL;
  if (configured) return configured;
  if (import.meta.env?.MODE === "demo") {
    return typeof globalThis.location?.origin === "string" &&
      globalThis.location.origin.startsWith("http")
      ? globalThis.location.origin
      : "/";
  }
  if (
    import.meta.env?.VITE_DEMO_MODE === "true" &&
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

async function dispatchWithCurrentBaseUrl(
  initialBaseUrl: string,
  input: RequestInfo | URL,
  init?: RequestInit
) {
  const currentBase = getBaseUrl().replace(/\/+$/, "");
  const initialBase = initialBaseUrl.replace(/\/+$/, "");
  if (
    currentBase !== initialBase &&
    input instanceof Request &&
    input.url.startsWith(`${initialBase}/`)
  ) {
    const rewrittenUrl = `${currentBase}${input.url.slice(initialBase.length)}`;
    const body =
      input.method !== "GET" && input.method !== "HEAD"
        ? await input.clone().arrayBuffer()
        : undefined;
    return globalThis.fetch(
      new Request(rewrittenUrl, {
        method: input.method,
        headers: input.headers,
        body: body && body.byteLength > 0 ? body : undefined,
        credentials: input.credentials,
        mode: input.mode,
        cache: input.cache,
        redirect: input.redirect,
        referrer: input.referrer,
        referrerPolicy: input.referrerPolicy,
        integrity: input.integrity,
        keepalive: input.keepalive,
        signal: input.signal,
      }),
      init
    );
  }
  return globalThis.fetch(input, init);
}

const INITIAL_BASE_URL = getBaseUrl();

export const openApiClient = createClient<paths>({
  baseUrl: INITIAL_BASE_URL,
  fetch: (input: Request, init?: RequestInit) =>
    dispatchWithCurrentBaseUrl(INITIAL_BASE_URL, input, init),
});

openApiClient.use(getAuthMiddleware());

export function createOpenApiClient<T extends {} = paths>(baseUrl?: string) {
  const initialBaseUrl = baseUrl ?? getBaseUrl();
  // Resolve fetch (and dynamic default baseUrl when initialized across circular imports) per call,
  // so tests, Vite dev env injection, and the MSW worker see the current environment.
  const client = createClient<T>({
    baseUrl: initialBaseUrl,
    fetch:
      baseUrl !== undefined
        ? (...args) => globalThis.fetch(...args)
        : (input: Request, init?: RequestInit) =>
            dispatchWithCurrentBaseUrl(initialBaseUrl, input, init),
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
