import { createApp, h, ref } from "vue";
import { ClinicalToastHost } from "ui";

let nextToastId = 1;
let standaloneApp = null;
let standaloneMountEl = null;

export const portalToasts = ref([]);

export function dismissToast(id) {
  portalToasts.value = portalToasts.value.filter((item) => item.id !== id);
}

export function clearToasts() {
  portalToasts.value = [];
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
          position: "bottom-right",
          toasts: portalToasts.value,
          latestToastId: "toast-container",
          ariaLabel: "Participant notifications",
          onDismiss: dismissToast,
          onClear: () => {
            portalToasts.value = [];
          },
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
    portalToasts.value = [];
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

  const id = nextToastId++;
  portalToasts.value = [
    ...portalToasts.value,
    {
      id,
      tone: variant === "danger" ? "error" : variant,
      variant,
      title,
      message: String(message ?? ""),
      timeoutMs,
    },
  ];

  ensurePortalToastHostMounted();
  return id;
}
