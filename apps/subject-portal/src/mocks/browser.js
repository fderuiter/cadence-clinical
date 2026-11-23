import { setupWorker } from "msw/browser";
import {
  createMswWorkerStarter,
  ensureServiceWorkerController,
  normalizeMswBasePath,
  resolveMswServiceWorkerOptions,
} from "ui/testing";
import { isDemoBuild } from "../utils/demoMode.js";
import { gatewayHandlers } from "./handlers.js";

export { ensureServiceWorkerController };

export function normalizePortalBasePath(
  baseUrl = import.meta.env?.BASE_URL || "/subject-portal/"
) {
  return normalizeMswBasePath(baseUrl, "/subject-portal/");
}

export function resolvePortalServiceWorkerOptions(
  baseUrl = import.meta.env?.BASE_URL
) {
  return resolveMswServiceWorkerOptions(baseUrl, {
    workerScript: "sw.js?msw=1",
    fallbackBase: "/subject-portal/",
  });
}

const portalWorker =
  typeof window !== "undefined" && import.meta.env?.MODE !== "test"
    ? (() => {
        try {
          return setupWorker(...gatewayHandlers);
        } catch {
          return null;
        }
      })()
    : null;

export const worker = portalWorker;

export const startMswWorker = createMswWorkerStarter({
  getWorker: () => worker,
  resolveServiceWorkerOptions: resolvePortalServiceWorkerOptions,
  isDemoBuild,
  startedLogMessage:
    "[MSW] Mock Service Worker started for Subject Portal API Gateway simulation.",
  warningLogPrefix: "[MSW] Subject portal service worker startup warning:",
});
