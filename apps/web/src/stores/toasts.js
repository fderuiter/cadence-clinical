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

/**
 * Transient user-facing alerts. API failures are reported here instead of being replaced with sample data
 * (#4523); ToastHost.vue renders them.
 */
const rawUseToastStore = defineStore("toasts", {
  state: () => ({ toasts: [] }),
  actions: {
    push({
      tone = "error",
      title,
      message,
      action = null,
      targets = null,
      timeoutMs = 8000,
    }) {
      const id = nextId++;
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
        tone,
        title,
        message,
        action: normalizedAction,
        targets: normalizedTargets,
      });
      if (timeoutMs > 0 && typeof window !== "undefined") {
        window.setTimeout(() => this.dismiss(id), timeoutMs);
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
    dismiss(id) {
      this.toasts = this.toasts.filter((t) => t.id !== id);
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
