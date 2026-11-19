import { isDemoModeActive } from "../utils/demoMode.js";

/**
 * Screens available outside explicit demo builds (docs/web/API_MIGRATION.md).
 *
 * Participant wallet and plain-language trial results have no live .NET
 * endpoints and `subject` holds zero payments permissions
 * (PermissionMatrix.Payments.cs), so they remain hidden from navigation and
 * redirect direct visits to UnavailableView outside demo builds.
 */
export const AVAILABLE_SCREENS = new Set([
  "tasks",
  "questionnaire",
  "compliance",
  "inbox",
  "consent",
]);

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
  return AVAILABLE_SCREENS.has(routeName);
}
