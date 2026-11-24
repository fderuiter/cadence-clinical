import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";

/**
 * Screens available to any session outside explicit demo builds (docs/web/API_MIGRATION.md).
 *
 * Python-era ePRO (`tasks`, `questionnaire`, `compliance`, `inbox`) and
 * plain-language trial results (`lay-summaries`) have no .NET backend yet and
 * live in `DEMO_AVAILABLE_SCREENS` (ADR-2819). Participant wallet (`wallet`)
 * and electronic consent (`consent`) are enabled for authenticated participants
 * via caller-bound self-service endpoints (`/api/v1/payments/participant/me/*`
 * and `/api/v1/econsent/*`) and in explicit demo builds.
 */
export const AVAILABLE_SCREENS = new Set();

export const DEMO_AVAILABLE_SCREENS = new Set([
  "tasks",
  "questionnaire",
  "compliance",
  "inbox",
  "consent",
  "lay-summaries",
]);

export const AUTHENTICATED_SCREENS = new Set(["wallet", "consent"]);

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
  if (isDemoModeActive(env)) {
    return (
      DEMO_AVAILABLE_SCREENS.has(routeName) ||
      AUTHENTICATED_SCREENS.has(routeName) ||
      AVAILABLE_SCREENS.has(routeName)
    );
  }
  if (AUTHENTICATED_SCREENS.has(routeName)) {
    return Boolean(
      state.session?.token &&
      state.session?.userId &&
      !state.session?.isOfflineMode
    );
  }
  return AVAILABLE_SCREENS.has(routeName);
}
