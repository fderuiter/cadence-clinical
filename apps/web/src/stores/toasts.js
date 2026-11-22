import { defineStore } from "pinia";
import { describeApiError } from "../utils/apiErrors.js";

let nextId = 1;

/**
 * Transient user-facing alerts. API failures are reported here instead of being replaced with sample data
 * (#4523); ToastHost.vue renders them.
 */
export const useToastStore = defineStore("toasts", {
  state: () => ({
    /** @type {Array<{ id: number; tone: string; title: string; message: string }>} */
    toasts: [],
  }),
  actions: {
    push({ tone = "error", title, message, timeoutMs = 8000 }) {
      const id = nextId++;
      this.toasts.push({ id, tone, title, message });
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
      return this.push({
        tone: "error",
        title,
        message: describeApiError(err),
        timeoutMs: 0,
      });
    },
    dismiss(id) {
      this.toasts = this.toasts.filter((t) => t.id !== id);
    },
  },
});
