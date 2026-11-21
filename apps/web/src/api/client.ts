import { resolveGatewayBaseUrl } from "../utils/demoMode.js";
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
  const env = import.meta.env as Record<string, string | undefined> | undefined;
  const configured = env?.VITE_API_BASE_URL;
  if (configured) return configured;
  return resolveGatewayBaseUrl();
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

async function rebuildRequest(
  source: Request,
  url: string = source.url,
  headers: HeadersInit = source.headers
): Promise<Request> {
  const hasBody =
    source.body !== null && source.method !== "GET" && source.method !== "HEAD";
  const body = hasBody ? await source.arrayBuffer() : undefined;
  return new Request(url, {
    method: source.method,
    headers,
    body: body && body.byteLength > 0 ? body : undefined,
    credentials: source.credentials,
    mode: source.mode,
    cache: source.cache,
    redirect: source.redirect,
    referrer: source.referrer,
    referrerPolicy: source.referrerPolicy,
    integrity: source.integrity,
    keepalive: source.keepalive,
    signal: source.signal,
  });
}

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
    const retriedRequest = await rebuildRequest(
      retryClone,
      retryClone.url,
      retryHeaders
    );
    return globalThis.fetch(retriedRequest, init);
  }
  const retryHeaders = new Headers(init?.headers);
  retryHeaders.set("Authorization", `Bearer ${freshToken}`);
  return globalThis.fetch(input, { ...init, headers: retryHeaders });
}

async function dispatchWithDynamicBase(
  initialBaseUrl: string,
  input: RequestInfo | URL,
  init?: RequestInit
): Promise<Response> {
  const currentBase = getBaseUrl().replace(/\/+$/, "");
  const initialBase = initialBaseUrl.replace(/\/+$/, "");
  if (
    currentBase !== initialBase &&
    input instanceof Request &&
    input.url.startsWith(`${initialBase}/`)
  ) {
    const rewrittenUrl = `${currentBase}${input.url.slice(initialBase.length)}`;
    const rewrittenRequest = await rebuildRequest(input.clone(), rewrittenUrl);
    return executeWithSingleFlightRetry(rewrittenRequest, init);
  }
  return executeWithSingleFlightRetry(input, init);
}

export function createOpenApiClient<T extends {} = paths>(baseUrl?: string) {
  const hasExplicitBaseUrl = baseUrl !== undefined;
  let inner: ReturnType<typeof createClient<T>> | null = null;
  const pendingMiddleware: Middleware[] = [];

  const getInner = () => {
    if (!inner) {
      const initialClientBase = baseUrl ?? getBaseUrl();
      inner = createClient<T>({
        baseUrl: initialClientBase,
        fetch: (input: RequestInfo | URL, init?: RequestInit) =>
          hasExplicitBaseUrl
            ? executeWithSingleFlightRetry(input, init)
            : dispatchWithDynamicBase(initialClientBase, input, init),
      });
      inner.use(getAuthMiddleware());
      for (const mw of pendingMiddleware) {
        inner.use(mw);
      }
    }
    return inner;
  };

  return new Proxy({} as ReturnType<typeof createClient<T>>, {
    get(_target, prop, receiver) {
      if (prop === "use" && !inner) {
        return (...middlewares: Middleware[]) => {
          pendingMiddleware.push(...middlewares);
        };
      }
      const target = getInner();
      const value = Reflect.get(target, prop, receiver);
      return typeof value === "function" ? value.bind(target) : value;
    },
  });
}

export const openApiClient = createOpenApiClient<paths>();

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
