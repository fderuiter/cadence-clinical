import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";

/**
 * Screens available to any session outside explicit demo builds (docs/web/API_MIGRATION.md).
 *
 * Unbacked participant sections (`tasks`, `questionnaire`, `compliance`,
 * `inbox`, `consent`, and `lay-summaries`) remain gated to explicit demo mode
 * until live participant interop, eConsent, and trial summary endpoints are
 * mounted. Participant wallet (`wallet`) is enabled for authenticated
 * participants via signed self-service endpoints (`/api/v1/payments/participant/me/*`)
 * and in explicit demo builds.
 */
export const AVAILABLE_SCREENS = new Set([]);

export const AUTHENTICATED_SCREENS = new Set(["wallet"]);

const ALWAYS_AVAILABLE = new Set(["unavailable"]);

export const UNAVAILABLE_SCREENS_ISSUE =
  "https://github.com/fderuiter/cadence-clinical/issues/4829";

const SCREEN_TITLES = {
  tasks: "My Tasks",
  questionnaire: "Questionnaire",
  compliance: "My Compliance",
  inbox: "My Inbox",
  consent: "My Consent",
  wallet: "Participant Financial Wallet",
  "lay-summaries": "Plain-Language Clinical Trial Results",
};

export function screenTitle(routeName) {
  return SCREEN_TITLES[routeName] || "This workflow";
}

export function isScreenAvailable(routeName, env = import.meta.env) {
  if (!routeName) return false;
  if (ALWAYS_AVAILABLE.has(routeName)) return true;
  if (isDemoModeActive(env)) return true;
  if (AUTHENTICATED_SCREENS.has(routeName)) {
    return Boolean(
      state.session?.token &&
      state.session?.userId &&
      !state.session?.isOfflineMode
    );
  }
  return AVAILABLE_SCREENS.has(routeName);
}

export function defaultPortalRouteName(env = import.meta.env) {
  if (isScreenAvailable("tasks", env)) return "tasks";
  if (isScreenAvailable("wallet", env)) return "wallet";
  return "unavailable";
}

export function defaultPortalViewId(env = import.meta.env) {
  const routeName = defaultPortalRouteName(env);
  if (routeName === "tasks") return "view-tasks";
  if (routeName === "wallet") return "view-wallet";
  return "view-unavailable";
}
