import { setupWorker } from "msw/browser";
import {
  ensureMswServiceWorkerReady,
  normalizeMswBasePath,
  waitForMswController,
  withMswStartTimeout,
} from "ui/testing";
import { isDemoBuild } from "../utils/demoMode.js";
import { gatewayHandlers } from "./handlers.js";

export function normalizePortalBasePath(
  baseUrl = import.meta.env?.BASE_URL || "/subject-portal/"
) {
  return normalizeMswBasePath(baseUrl, "/subject-portal/");
}

export function resolvePortalServiceWorkerOptions(
  baseUrl = import.meta.env?.BASE_URL
) {
  const scope = normalizePortalBasePath(baseUrl);
  return {
    url: `${scope}sw.js?msw=1`,
    options: { scope },
  };
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

let portalStartTask = null;

export async function startMswWorker(portalOptions = {}) {
  const targetWorker = portalOptions.worker ?? worker;
  if (typeof window === "undefined" || !targetWorker) return null;
  if (portalStartTask && !portalOptions.worker) return portalStartTask;

  const serviceWorker = resolvePortalServiceWorkerOptions(
    portalOptions.baseUrl
  );

  const task = (async () => {
    try {
      if (!portalOptions.worker) {
        await ensureMswServiceWorkerReady(serviceWorker);
      }
      const startCall = targetWorker.start({
        onUnhandledFrame: isDemoBuild(portalOptions.env) ? "error" : "warn",
        quiet: Boolean(portalOptions.quiet),
        serviceWorker,
      });
      const reg = portalOptions.worker
        ? await startCall
        : await withMswStartTimeout(startCall);
      await waitForMswController();
      console.log(
        "[MSW] Mock Service Worker started for Subject Portal API Gateway simulation."
      );
      return reg;
    } catch (error) {
      portalStartTask = null;
      if (import.meta.env?.MODE !== "test") {
        console.warn(
          "[MSW] Subject portal service worker startup warning:",
          error
        );
      }
      return null;
    }
  })();

  if (!portalOptions.worker) {
    portalStartTask = task;
  }
  return task;
}
