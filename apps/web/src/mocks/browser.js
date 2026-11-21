import { setupWorker } from "msw/browser";
import { normalizeMswBasePath, waitForMswController } from "ui/testing";
import { isDemoBuild } from "../utils/demoMode.js";
import { gatewayHandlers } from "./handlers.js";

export function normalizeBasePath(baseUrl = import.meta.env?.BASE_URL) {
  return normalizeMswBasePath(baseUrl, "/");
}

export function resolveWebServiceWorkerOptions(
  baseUrl = import.meta.env?.BASE_URL
) {
  const scope = normalizeBasePath(baseUrl);
  return {
    url: `${scope}mockServiceWorker.js`,
    options: { scope },
  };
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

let startPromise = null;

export async function ensureServiceWorkerController(timeoutMs = 1500) {
  if (
    typeof navigator === "undefined" ||
    !("serviceWorker" in navigator) ||
    typeof navigator.serviceWorker?.addEventListener !== "function" ||
    navigator.serviceWorker.controller
  ) {
    return;
  }
  await new Promise((resolve) => {
    let timer = null;
    const done = () => {
      if (timer !== null) {
        clearTimeout(timer);
      }
      navigator.serviceWorker?.removeEventListener?.("controllerchange", done);
      resolve();
    };
    navigator.serviceWorker.addEventListener("controllerchange", done, {
      once: true,
    });
    if (navigator.serviceWorker.controller) {
      done();
      return;
    }
    timer = setTimeout(done, timeoutMs);
  });
}

export async function startMswWorker(customOptions = {}) {
  const activeWorker = customOptions.worker ?? worker;
  if (typeof window === "undefined" || !activeWorker) return null;
  if (startPromise && !customOptions.worker) return startPromise;

  const serviceWorker = resolveWebServiceWorkerOptions(customOptions.baseUrl);
  const onUnhandledFrame = isDemoBuild(customOptions.env) ? "error" : "warn";

  const runWebWorkerStart = async () => {
    try {
      const registration = await activeWorker.start({
        onUnhandledFrame,
        quiet: Boolean(customOptions.quiet),
        serviceWorker,
      });
      await waitForMswController();
      await ensureServiceWorkerController();
      console.log(
        "[MSW] Mock Service Worker started for Web App API Gateway simulation."
      );
      return registration;
    } catch (err) {
      startPromise = null;
      if (import.meta.env?.MODE !== "test") {
        console.warn("[MSW] Web app service worker startup warning:", err);
      }
      return null;
    }
  };

  if (customOptions.worker) return runWebWorkerStart();
  startPromise = runWebWorkerStart();
  return startPromise;
}
