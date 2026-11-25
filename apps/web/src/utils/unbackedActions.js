import { createUnbackedActionGuard } from "demo-fixtures/runtime";
import { useToastStore } from "../stores/toasts.js";
import { isDemoBuild } from "./demoMode.js";

/**
 * Regulated actions that no Cadence service records yet (#4827): enrolment, re-consent, SDV, query
 * transitions, electronic signatures, adjudication, exports and the like. Recording them only in the
 * browser and reporting success would tell a user that a GxP record exists when it does not.
 *
 * Remove the call when the action is moved onto a real endpoint (docs/web/API_MIGRATION.md).
 */
const webUnbackedActionGuard = createUnbackedActionGuard({
  isAllowed: (env) => (env === undefined ? isDemoBuild() : isDemoBuild(env)),
  defaultDetail:
    "No Cadence service records this action yet, so nothing was saved.",
  onRefuse({ title, detail }) {
    useToastStore().push({
      tone: "error",
      title,
      message: detail,
      timeoutMs: 0,
    });
  },
});

export const {
  allowUnbackedAction,
  refuseUnbackedAction,
  unbackedActionsEnabled,
} = webUnbackedActionGuard;
