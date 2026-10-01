import { setupWorker } from "msw/browser";
import { gatewayHandlers } from "./handlers.js";

let workerInstance = null;
try {
  if (typeof window !== "undefined" && import.meta.env?.MODE !== "test") {
    workerInstance = setupWorker(...gatewayHandlers);
  }
} catch {
  // Ignored in jsdom/node test environment
}

export const worker = workerInstance;

export async function startMswWorker() {
  if (typeof window === "undefined" || !worker) return;
  try {
    await worker.start({
      onUnhandledRequest: "bypass",
      serviceWorker: {
        url: "/mockServiceWorker.js",
      },
    });
    console.log(
      "[MSW] Mock Service Worker started for API Gateway simulation."
    );
  } catch (err) {
    if (import.meta.env?.MODE !== "test") {
      console.warn("[MSW] Service Worker startup warning:", err);
    }
  }
}
