import { createApp, h } from "vue";
import { ClinicalToastHost, createToastController } from "ui";

let standaloneApp = null;
let standaloneMountEl = null;

export const portalToastController = createToastController({
  defaultTone: "success",
  defaultTimeoutMs: 3500,
  apiErrorTimeoutMs: 0,
  persistErrors: true,
});

export const portalToasts = portalToastController.toasts;

export function dismissToast(id) {
  return portalToastController.dismiss(id);
}

export function clearToasts() {
  portalToastController.clear();
  if (standaloneApp) {
    try {
      standaloneApp.unmount();
    } catch {
      // ignore already unmounted host
    }
    standaloneApp = null;
  }
  if (standaloneMountEl && standaloneMountEl.parentNode) {
    standaloneMountEl.parentNode.removeChild(standaloneMountEl);
  }
  standaloneMountEl = null;
}

function ensurePortalToastHostMounted() {
  if (typeof document === "undefined" || !document.body) return false;
  const existingHost = document.body.querySelector(
    '[data-portal-toast-host="true"]'
  );
  if (existingHost) {
    return false;
  }

  // If a previous test replaced document.body.innerHTML, clean up any detached standalone app
  // and clear stale entries from the detached DOM session before mounting a fresh host.
  if (standaloneApp) {
    try {
      standaloneApp.unmount();
    } catch {
      // ignore
    }
    standaloneApp = null;
    standaloneMountEl = null;
  }

  const mountEl = document.createElement("div");
  mountEl.setAttribute("data-portal-toast-mount", "true");
  document.body.appendChild(mountEl);
  standaloneMountEl = mountEl;

  standaloneApp = createApp({
    name: "PortalStandaloneToastHost",
    setup() {
      return () =>
        h(ClinicalToastHost, {
          "data-portal-toast-host": "true",
          controller: portalToastController,
          variantStyle: "portal",
          latestToastId: "toast-container",
          ariaLabel: "Participant notifications",
        });
    },
  });
  standaloneApp.mount(mountEl);
  return true;
}

export function showToast(message, options = {}, durationArg = undefined) {
  if (typeof document === "undefined" || !document.body) return null;

  const hasConnectedHost = Boolean(
    document.body.querySelector('[data-portal-toast-host="true"]')
  );
  if (
    !hasConnectedHost &&
    standaloneMountEl &&
    !document.body.contains(standaloneMountEl)
  ) {
    portalToastController.clear();
  }

  const rawVariant =
    typeof options === "string"
      ? options
      : options?.variant || options?.tone || "success";
  const variant = String(rawVariant).toLowerCase();
  const title =
    typeof options === "object" && typeof options?.title === "string"
      ? options.title.trim()
      : "";
  const action =
    typeof options === "object" && options?.action ? options.action : null;
  const requestedDuration =
    typeof durationArg === "number"
      ? durationArg
      : typeof options === "number"
        ? options
        : typeof options === "object" && typeof options?.duration === "number"
          ? options.duration
          : typeof options === "object" &&
              typeof options?.timeoutMs === "number"
            ? options.timeoutMs
            : 3500;
  const timeoutMs =
    variant === "error" || variant === "danger"
      ? 0
      : Math.max(0, requestedDuration);

  const id = portalToastController.push({
    tone: variant === "danger" ? "error" : variant,
    variant,
    title,
    message: String(message ?? ""),
    timeoutMs,
    action,
  });

  ensurePortalToastHostMounted();
  return id;
}
