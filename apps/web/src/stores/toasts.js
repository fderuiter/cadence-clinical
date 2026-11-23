import {
  createPinia,
  defineStore,
  getActivePinia,
  setActivePinia,
} from "pinia";
import { createToastController } from "ui";
import { describeApiError } from "../utils/apiErrors.js";

/**
 * Transient and persistent user-facing notifications backed by shared `createToastController` (`ui`).
 * Urgent `error` alerts persist in `role="alert"` (`aria-live="assertive"`) until dismissed;
 * routine `success`, `warning`, and `info` confirmations announce politely via `role="status"`
 * (`aria-live="polite"`), pause on hover/focus/explicit toggle, and scale with `useTimingPreferences`.
 */
const rawUseToastStore = defineStore("toasts", () =>
  createToastController({
    defaultTone: "error",
    defaultTimeoutMs: 8000,
    apiErrorTimeoutMs: 0,
    persistErrors: true,
    formatError: describeApiError,
  })
);

/** @type {typeof rawUseToastStore} */
export const useToastStore = Object.assign((pinia) => {
  if (!pinia && !getActivePinia()) {
    setActivePinia(createPinia());
  }
  return rawUseToastStore(pinia);
}, rawUseToastStore);
