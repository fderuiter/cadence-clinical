import {
  createPinia,
  defineStore,
  getActivePinia,
  setActivePinia,
} from "pinia";
import {
  describeApiError,
  extractProblemExplanation,
} from "../utils/apiErrors.js";

let nextId = 1;
const toastTimers = new Map();

function clearToastTimer(id) {
  const entry = toastTimers.get(id);
  if (!entry) return;
  if (entry.timerId !== null) {
    clearTimeout(entry.timerId);
  }
  toastTimers.delete(id);
}

/**
 * Transient and persistent user-facing alerts. API and validation errors persist until
 * explicitly dismissed; non-error status messages auto-dismiss and pause on hover/focus.
 */
const rawUseToastStore = defineStore("toasts", {
  state: () => ({
    /** @type {Array<{ id: number; tone: string; title: string; message: string }>} */
    toasts: [],
  }),
  actions: {
    /**
     * @param {{
     *   tone?: string;
     *   title?: string;
     *   message?: string;
     *   action?: { label: string; to: string } | null;
     *   targets?: Array<{ target: string; description?: string }> | null;
     *   timeoutMs?: number;
     * }} [options]
     */
    push({
      tone = "error",
      title = "",
      message = "",
      action = null,
      targets = null,
      timeoutMs = undefined,
    } = {}) {
      const id = nextId++;
      const normalizedTone =
        typeof tone === "string" && tone.trim()
          ? tone.trim().toLowerCase()
          : "error";
      const normalizedTitle = typeof title === "string" ? title.trim() : "";
      const effectiveTimeoutMs =
        normalizedTone === "error"
          ? 0
          : timeoutMs === undefined
            ? 8000
            : Math.max(0, Number(timeoutMs) || 0);
      const normalizedAction =
        action &&
        typeof action === "object" &&
        typeof action.label === "string" &&
        action.label.trim() &&
        typeof action.to === "string" &&
        action.to.trim()
          ? { label: action.label.trim(), to: action.to.trim() }
          : null;
      const normalizedTargets = Array.isArray(targets)
        ? targets
            .filter((t) => t && typeof t.target === "string" && t.target.trim())
            .map((t) => ({
              target: t.target.trim(),
              description:
                typeof t.description === "string" ? t.description.trim() : "",
            }))
        : [];
      this.toasts.push({
        id,
        tone: normalizedTone,
        title: normalizedTitle,
        message: message ?? "",
        action: normalizedAction,
        targets: normalizedTargets,
        timeoutMs: effectiveTimeoutMs,
      });

      if (effectiveTimeoutMs > 0 && typeof setTimeout === "function") {
        const timerId = setTimeout(() => {
          toastTimers.delete(id);
          this.dismiss(id);
        }, effectiveTimeoutMs);
        toastTimers.set(id, {
          timerId,
          remainingMs: effectiveTimeoutMs,
          startedAt: Date.now(),
          paused: false,
        });
      }

      return id;
    },
    addToast(payload) {
      return this.push(payload);
    },
    /** Reports a failed API call: `title` says what failed, the message comes from the error. */
    apiError(title, err) {
      console.error(`${title}:`, err);
      const exp = extractProblemExplanation(err);
      return this.push({
        tone: "error",
        title,
        message: describeApiError(err),
        targets: exp?.nextActions ?? [],
        timeoutMs: 0,
      });
    },
    pause(id) {
      const entry = toastTimers.get(id);
      if (!entry || entry.paused) return;
      if (entry.timerId !== null) {
        clearTimeout(entry.timerId);
        entry.timerId = null;
      }
      const elapsed = Date.now() - entry.startedAt;
      entry.remainingMs = Math.max(1, entry.remainingMs - elapsed);
      entry.paused = true;
    },
    resume(id) {
      const entry = toastTimers.get(id);
      if (!entry || !entry.paused) return;
      entry.paused = false;
      entry.startedAt = Date.now();
      entry.timerId = setTimeout(() => {
        toastTimers.delete(id);
        this.dismiss(id);
      }, entry.remainingMs);
    },
    dismiss(id) {
      clearToastTimer(id);
      this.toasts = this.toasts.filter((t) => t.id !== id);
    },
    clear() {
      for (const id of Array.from(toastTimers.keys())) {
        clearToastTimer(id);
      }
      this.toasts = [];
    },
  },
});

/** @type {typeof rawUseToastStore} */
export const useToastStore = Object.assign((pinia) => {
  if (!pinia && !getActivePinia()) {
    setActivePinia(createPinia());
  }
  return rawUseToastStore(pinia);
}, rawUseToastStore);
