// View switching. `state.currentView` (the ui store) is the source of truth the
// shell renders from; the router follows it into the URL and back (see
// src/router/index.js).
import { isScreenAvailable } from "../router/availability.js";
import { activeTabForView, routeForView, tabs } from "../router/routes.js";
import { state } from "../stores/state.js";
import { loadConsentDetails } from "./consent.js";

const viewLeaveHandlers = new Map();

export function registerViewLeaveHandler(viewId, handler) {
  if (typeof handler === "function") {
    viewLeaveHandlers.set(viewId, handler);
  }
}

// Activates a view: enforces screen availability outside demo builds, invokes
// leave callbacks, updates `.active`/`hidden`/`inert` attributes and
// single-source tab ARIA attributes, and loads consent details when opened.
export function showView(viewId) {
  const matchedRoute = routeForView(viewId);
  let targetViewId = viewId;

  if (matchedRoute && !isScreenAvailable(matchedRoute.name)) {
    state.unavailableScreen = String(matchedRoute.name);
    targetViewId = "view-unavailable";
  }

  const previousView = state.currentView;
  if (previousView && previousView !== targetViewId) {
    const leaveHandler = viewLeaveHandlers.get(previousView);
    if (typeof leaveHandler === "function") {
      try {
        leaveHandler(targetViewId);
      } catch (err) {
        console.warn(`Error running leave handler for ${previousView}:`, err);
      }
    }
  }

  state.currentView = targetViewId;

  if (typeof document !== "undefined") {
    document.querySelectorAll(".portal-view").forEach((v) => {
      const isActive = v.id === targetViewId;
      v.classList.toggle("active", isActive);
      v.hidden = !isActive;
      if (isActive) {
        v.removeAttribute("hidden");
        v.removeAttribute("inert");
        if ("inert" in v) {
          v.inert = false;
        }
      } else {
        v.setAttribute("hidden", "");
        v.setAttribute("inert", "");
        if ("inert" in v) {
          v.inert = true;
        }
      }
    });

    const questionnaireSection = document.getElementById("view-questionnaire");
    if (questionnaireSection) {
      if (!questionnaireSection.hasAttribute("role")) {
        questionnaireSection.setAttribute("role", "tabpanel");
      }
      if (!questionnaireSection.hasAttribute("tabindex")) {
        questionnaireSection.setAttribute("tabindex", "0");
      }
      const hasTasksTab = Boolean(document.getElementById("btn-tab-btn-tasks"));
      questionnaireSection.setAttribute(
        "aria-labelledby",
        hasTasksTab ? "btn-tab-btn-tasks" : "questionnaire-title"
      );
    }

    const activeTab = activeTabForView(targetViewId);
    for (const tab of tabs) {
      const isSelected = activeTab?.id === tab.id;
      const item = document.getElementById(tab.itemId);
      if (item) {
        item.classList.toggle("active", isSelected);
      }
      const btn =
        document.getElementById(tab.buttonId) || item?.querySelector("button");
      if (btn) {
        btn.setAttribute("aria-selected", isSelected ? "true" : "false");
        btn.setAttribute("tabindex", isSelected ? "0" : "-1");
        if (btn.hasAttribute("aria-controls")) {
          btn.setAttribute(
            "aria-controls",
            tab.id === "tasks" && targetViewId === "view-questionnaire"
              ? "view-questionnaire"
              : tab.viewId
          );
        }
      }
    }
  }

  if (
    targetViewId === "view-consent" &&
    typeof loadConsentDetails === "function"
  ) {
    loadConsentDetails();
  }
}
