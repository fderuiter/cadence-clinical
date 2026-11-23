// View switching. `state.currentView` (the ui store) is the source of truth the
// shell renders from; the router follows it into the URL and back (see
// src/router/index.js).
import { handoffFocus } from "ui";
import {
  formatPortalViewAnnouncement,
  isScreenAvailable,
  resolvePortalViewTitle,
} from "../router/availability.js";
import { activeTabForView, routeForView, tabs } from "../router/routes.js";
import { state } from "../stores/state.js";
import { loadConsentDetails } from "./consent.js";

const viewLeaveHandlers = new Map();

export function registerViewLeaveHandler(viewId, handler) {
  if (typeof handler === "function") {
    viewLeaveHandlers.set(viewId, handler);
  }
}

function escapeSelectorId(id) {
  if (typeof CSS !== "undefined" && typeof CSS.escape === "function") {
    return CSS.escape(id);
  }
  return String(id).replace(/[^a-zA-Z0-9_-]/g, "\\$&");
}

function hasActiveModalOrDrawer() {
  return Boolean(
    state.signatureModalOpen ||
    state.reconsentModalOpen ||
    state.isSyncDrawerOpen ||
    state.disburseModalOpen ||
    state.questionnaireCancelConfirmOpen ||
    state.session?.pinSetup?.isOpen ||
    state.session?.pinUnlock?.isOpen
  );
}

// Activates a view: enforces screen availability outside demo builds, invokes
// leave callbacks, updates `.active`/`hidden`/`inert` attributes and
// single-source tab ARIA attributes, hands off focus to the active view, and
// loads consent details when opened.
export function showView(viewId, options = {}) {
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
    document.title = resolvePortalViewTitle(
      targetViewId,
      state.unavailableScreen
    );
  }

  if (
    !options.silent &&
    !state.isBootstrappingRoute &&
    previousView &&
    previousView !== targetViewId
  ) {
    state.routeAnnouncement = formatPortalViewAnnouncement(
      targetViewId,
      state.unavailableScreen
    );
  }

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

    if (!options.preserveTabFocus && !hasActiveModalOrDrawer()) {
      const escapedId = escapeSelectorId(targetViewId);
      void handoffFocus([
        options.focusTarget || null,
        () => document.querySelector(`#${escapedId} .view-header h2`),
        () => document.querySelector(`#${escapedId} h2`),
        `#${escapedId}`,
        "#main-content",
      ]);
    }
  }

  if (
    targetViewId === "view-consent" &&
    typeof loadConsentDetails === "function"
  ) {
    loadConsentDetails();
  }
}
