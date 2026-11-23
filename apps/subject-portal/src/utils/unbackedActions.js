import { createUnbackedActionGuard } from "demo-fixtures/runtime";
import { showToast } from "../widgets/toast.js";
import { isDemoModeActive } from "./demoMode.js";

/**
 * Regulated participant actions that no Cadence .NET service persists yet
 * (docs/web/API_MIGRATION.md, 21 CFR Part 11). Recording them only in the
 * browser outside an explicit demo build would falsely indicate that a GxP
 * clinical or financial record was saved.
 */
const portalUnbackedActionGuard = createUnbackedActionGuard({
  isAllowed: (env) => isDemoModeActive(env),
  defaultDetail:
    "No live Cadence service records this action yet, so nothing was saved.",
  onRefuse({ message }) {
    showToast(message, "error");
    return message;
  },
});

export const {
  allowUnbackedAction,
  refuseUnbackedAction,
  unbackedActionsEnabled,
} = portalUnbackedActionGuard;
