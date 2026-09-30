import { isDemoBuild } from "./demoMode.js";
import { useToastStore } from "../stores/toasts.js";

/**
 * Regulated actions that no Cadence service records yet (#4827): enrolment, re-consent, SDV, query
 * transitions, electronic signatures, adjudication, exports and the like. Recording them only in the
 * browser and reporting success would tell a user that a GxP record exists when it does not.
 *
 * Call this at the start of such an action. In a demo build it returns true and the action runs on local
 * demo data as before. In every other build it refuses: it tells the user the action isn't available yet
 * and that nothing was saved, and returns false so the caller stops before changing any state.
 *
 * Remove the call when the action is moved onto a real endpoint (docs/web/API_MIGRATION.md).
 */
export function allowUnbackedAction(action, env = import.meta.env) {
  if (isDemoBuild(env)) return true;

  useToastStore().push({
    tone: "error",
    title: `${action} isn't available yet`,
    message:
      "No Cadence service records this action yet, so nothing was saved.",
    timeoutMs: 0,
  });
  return false;
}

/** True when actions without a backend may run, for disabling their buttons up front. */
export function unbackedActionsEnabled(env = import.meta.env) {
  return isDemoBuild(env);
}
