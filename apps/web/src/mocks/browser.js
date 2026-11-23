import { getResponse, isPassthroughResponse } from "msw";
import { setupWorker } from "msw/browser";
import {
  createMswWorkerStarter,
  ensureServiceWorkerController,
  normalizeMswBasePath,
  resolveMswServiceWorkerOptions,
} from "ui/testing";
import { getBearerToken, isGatewayRequest } from "../api/authorization.js";
import { isDemoBuild } from "../utils/demoMode.js";
import { gatewayHandlers } from "./handlers.js";

export { ensureServiceWorkerController };

export function normalizeBasePath(baseUrl = import.meta.env?.BASE_URL) {
  return normalizeMswBasePath(baseUrl, "/");
}

export function resolveWebServiceWorkerOptions(
  baseUrl = import.meta.env?.BASE_URL
) {
  return resolveMswServiceWorkerOptions(baseUrl, {
    workerScript: "mockServiceWorker.js",
    fallbackBase: "/",
  });
}

let workerInstance = null;
if (typeof window !== "undefined" && import.meta.env?.MODE !== "test") {
  try {
    workerInstance = setupWorker(...gatewayHandlers);
  } catch {
    // Ignored in jsdom/node test environment
  }
}

export const worker = workerInstance;

export function installInProcessMswFallback(target = window) {
  if (
    !target ||
    typeof target.fetch !== "function" ||
    target.fetch.__cadenceMswFallback
  ) {
    return;
  }
  const previousFetch = target.fetch.bind(target);
  const wrappedFetch = async (input, init) => {
    const baseHref =
      target.location?.href ||
      (typeof window !== "undefined" && window.location?.href) ||
      "https://cadence.example/";
    const rawUrl = input instanceof Request ? input.url : String(input);
    const resolvedUrl = new URL(rawUrl, baseHref).href;
    const baseReq =
      input instanceof Request
        ? new Request(resolvedUrl, input)
        : new Request(resolvedUrl, init);
    const headers = new Headers(init?.headers || baseReq.headers);
    if (
      isGatewayRequest(resolvedUrl, target.location) &&
      !headers.has("Authorization")
    ) {
      const token = await getBearerToken();
      if (token) {
        headers.set("Authorization", `Bearer ${token}`);
      }
    }
    const hasBody = baseReq.method !== "GET" && baseReq.method !== "HEAD";
    const bodyPayload =
      init?.body !== undefined
        ? init.body
        : hasBody
          ? await baseReq.clone().arrayBuffer()
          : undefined;
    const mswReq = new Request(resolvedUrl, {
      method: init?.method || baseReq.method,
      headers,
      body:
        bodyPayload instanceof ArrayBuffer && bodyPayload.byteLength === 0
          ? undefined
          : bodyPayload,
    });

    const mocked = await getResponse(gatewayHandlers, mswReq, {
      baseUrl: target.location?.origin,
    });
    if (!mocked || isPassthroughResponse(mocked)) {
      return previousFetch(input, init);
    }
    return mocked;
  };
  wrappedFetch.__cadenceMswFallback = true;
  target.fetch = wrappedFetch;
}

export const startMswWorker = createMswWorkerStarter({
  getWorker: () => worker,
  resolveServiceWorkerOptions: resolveWebServiceWorkerOptions,
  isDemoBuild,
  beforeStart(customOptions = {}) {
    const env = customOptions.env ?? import.meta.env;
    if (
      typeof window !== "undefined" &&
      !customOptions.worker &&
      env?.VITE_E2E_IN_PROCESS_MSW === "true"
    ) {
      installInProcessMswFallback(window);
      return { scope: normalizeBasePath(customOptions.baseUrl) };
    }
    return undefined;
  },
  startedLogMessage:
    "[MSW] Mock Service Worker started for Web App API Gateway simulation.",
  warningLogPrefix: "[MSW] Web app service worker startup warning:",
});
