import { showToast } from "../widgets/toast.js";
import { isDemoModeActive } from "./demoMode.js";

/**
 * Regulated participant actions that no Cadence .NET service persists yet
 * (docs/web/API_MIGRATION.md, 21 CFR Part 11). Recording them only in the
 * browser outside an explicit demo build would falsely indicate that a GxP
 * clinical or financial record was saved.
 */
export function allowUnbackedAction(action, env = import.meta.env) {
  if (isDemoModeActive(env)) {
    return true;
  }

  refuseUnbackedAction(action);
  return false;
}

export function refuseUnbackedAction(action, customDetail = null) {
  const detail =
    customDetail ||
    "No live Cadence service records this action yet, so nothing was saved.";
  const message = `${action} isn't available yet. ${detail}`;
  showToast(message, "error");
  return message;
}

export function unbackedActionsEnabled(env = import.meta.env) {
  return isDemoModeActive(env);
}
