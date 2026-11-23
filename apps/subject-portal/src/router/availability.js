import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";

/**
 * Screens available to any session outside explicit demo builds (docs/web/API_MIGRATION.md).
 *
 * Python-era ePRO (`tasks`, `questionnaire`, `compliance`, `inbox`), eConsent
 * (`consent`), and plain-language trial results (`lay-summaries`) have no .NET
 * backend yet and live in `DEMO_AVAILABLE_SCREENS` (ADR-2819). Participant
 * wallet (`wallet`) is enabled for authenticated participants via signed
 * self-service endpoints (`/api/v1/payments/participant/me/*`) and in explicit
 * demo builds.
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

export const PORTAL_BRAND_TITLE_SUFFIX = "Cadence Participant Portal";

/**
 * Study-centric, UX-1 compliant route titles for participant portal views.
 * Never expose internal product silo acronyms or participant PII.
 */
export const PORTAL_ROUTE_TITLES = Object.freeze({
  tasks: "Study Tasks & Daily Diaries",
  questionnaire: "Active Study Questionnaire",
  compliance: "Visit & Diary Completion",
  inbox: "Study Coordinator Messages",
  consent: "Informed Consent Review",
  wallet: "Visit Stipends & Reimbursements",
  "lay-summaries": "Plain-Language Study Results",
  unavailable: "Workflow Unavailable",
});

export const PORTAL_VIEW_TO_ROUTE = Object.freeze({
  "view-tasks": "tasks",
  "view-questionnaire": "questionnaire",
  "view-compliance": "compliance",
  "view-inbox": "inbox",
  "view-consent": "consent",
  "view-wallet": "wallet",
  "view-lay-summaries": "lay-summaries",
  "view-unavailable": "unavailable",
});

function cleanRouteToken(value) {
  if (typeof value !== "string") return "";
  return value
    .trim()
    .replace(/[\r\n\t]+/g, " ")
    .slice(0, 80);
}

export function screenTitle(routeName) {
  return SCREEN_TITLES[routeName] || "This workflow";
}

export function resolvePortalRouteLabel(routeOrName, unavailableScreen = "") {
  const routeName =
    typeof routeOrName === "string"
      ? cleanRouteToken(routeOrName)
      : cleanRouteToken(String(routeOrName?.name || ""));
  const queryScreen =
    typeof routeOrName === "object" && routeOrName !== null
      ? cleanRouteToken(
          String(routeOrName.query?.screen || unavailableScreen || "")
        )
      : cleanRouteToken(String(unavailableScreen || ""));

  if (routeName === "unavailable") {
    if (queryScreen) {
      const targetLabel =
        PORTAL_ROUTE_TITLES[queryScreen] || screenTitle(queryScreen);
      return `${targetLabel} (Unavailable)`;
    }
    return PORTAL_ROUTE_TITLES.unavailable;
  }

  if (routeName && PORTAL_ROUTE_TITLES[routeName]) {
    return PORTAL_ROUTE_TITLES[routeName];
  }

  return "Study Participant Workspace";
}

export function resolvePortalRouteTitle(routeOrName, unavailableScreen = "") {
  return `${resolvePortalRouteLabel(routeOrName, unavailableScreen)} — ${PORTAL_BRAND_TITLE_SUFFIX}`;
}

export function resolvePortalViewTitle(viewId, unavailableScreen = "") {
  const routeName = PORTAL_VIEW_TO_ROUTE[viewId] || viewId;
  return resolvePortalRouteTitle(routeName, unavailableScreen);
}

export function formatPortalRouteAnnouncement(
  routeOrName,
  unavailableScreen = ""
) {
  return `Navigated to ${resolvePortalRouteLabel(routeOrName, unavailableScreen)}`;
}

export function formatPortalViewAnnouncement(viewId, unavailableScreen = "") {
  const routeName = PORTAL_VIEW_TO_ROUTE[viewId] || viewId;
  return formatPortalRouteAnnouncement(routeName, unavailableScreen);
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
