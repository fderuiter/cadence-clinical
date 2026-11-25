import { encodeChangeReason } from "./changeReason";
import createClient, { type Middleware } from "openapi-fetch";
import {
  applyDemoIdentityHeaders,
  fetchWithSingleFlightRetry,
  getBearerToken,
  notifySubscriptionExpiredIfNeeded,
  rebuildRequest,
} from "./authorization.js";
import { ApiError } from "../utils/apiErrors.js";
import { isDemoBuild, resolveGatewayBaseUrl } from "../utils/demoMode";

export { ApiError };

export function getBaseUrl(): string {
  return resolveGatewayBaseUrl();
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
      if (isDemoBuild()) {
        applyDemoIdentityHeaders(request.headers);
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
    return fetchWithSingleFlightRetry(rewrittenRequest, init);
  }
  return fetchWithSingleFlightRetry(input, init);
}

async function normalizeFetchResponse(
  promise: Promise<Response>
): Promise<Response> {
  const response = await promise;
  if (
    response &&
    typeof response.text !== "function" &&
    typeof response.json === "function"
  ) {
    return Object.assign(
      Object.create(Object.getPrototypeOf(response)),
      response,
      {
        text: async () => {
          const payload = await response.json();
          return payload === undefined ? "" : JSON.stringify(payload);
        },
      }
    );
  }
  return response;
}

export function createOpenApiClient<T extends {} = Record<string, unknown>>(
  baseUrl?: string
) {
  const hasExplicitBaseUrl = baseUrl !== undefined;
  let inner: ReturnType<typeof createClient<T>> | null = null;
  const pendingMiddleware: Middleware[] = [];

  const getInner = () => {
    if (!inner) {
      const initialClientBase = baseUrl ?? getBaseUrl();
      inner = createClient<T>({
        baseUrl: initialClientBase,
        fetch: (input: RequestInfo | URL, init?: RequestInit) =>
          normalizeFetchResponse(
            hasExplicitBaseUrl
              ? fetchWithSingleFlightRetry(input, init)
              : dispatchWithDynamicBase(initialClientBase, input, init)
          ),
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

export async function unwrapResponse<T>(
  promise: Promise<{ data?: T; error?: unknown; response: Response }>
): Promise<T> {
  const { data, error, response } = await promise;
  if (error || !response.ok) {
    const errData = error || null;
    const expiredGuidance = notifySubscriptionExpiredIfNeeded(
      response?.status ?? null,
      errData
    );
    const detail =
      expiredGuidance ||
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
