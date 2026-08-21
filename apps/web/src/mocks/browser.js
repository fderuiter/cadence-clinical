import { setupWorker } from "msw/browser";
import { gatewayHandlers } from "./handlers.js";

export const worker = setupWorker(...gatewayHandlers);

export async function startMswWorker() {
  if (typeof window === "undefined") return;
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
    console.warn("[MSW] Service Worker startup warning:", err);
  }
}
