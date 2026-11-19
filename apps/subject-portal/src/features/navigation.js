// View switching. `state.currentView` (the ui store) is the source of truth the
// shell renders from; the router follows it into the URL and back.
import { isScreenAvailable } from "../router/availability.js";
import { routeForView } from "../router/routes.js";
import { state } from "../stores/state.js";
import { loadConsentDetails } from "./consent.js";

// Activates a view: enforces screen availability outside demo builds, updates
// `.active` classes and tab `aria-selected`, and loads consent details on open.
export function showView(viewId) {
  const matchedRoute = routeForView(viewId);
  let targetViewId = viewId;

  if (matchedRoute && !isScreenAvailable(matchedRoute.name)) {
    state.unavailableScreen = String(matchedRoute.name);
    targetViewId = "view-unavailable";
  }

  state.currentView = targetViewId;

  document.querySelectorAll(".portal-view").forEach((v) => {
    v.classList.remove("active");
  });
  document.querySelectorAll(".nav-item").forEach((item) => {
    item.classList.remove("active");
    const btn = item.querySelector("button");
    if (btn) {
      btn.setAttribute("aria-selected", "false");
    }
  });

  const targetView = document.getElementById(targetViewId);
  if (targetView) targetView.classList.add("active");

  let activeTabId = null;
  if (targetViewId === "view-tasks" || targetViewId === "view-questionnaire") {
    activeTabId = "tab-btn-tasks";
  } else if (targetViewId === "view-compliance") {
    activeTabId = "tab-btn-compliance";
  } else if (targetViewId === "view-inbox") {
    activeTabId = "tab-btn-inbox";
  } else if (targetViewId === "view-consent") {
    activeTabId = "tab-btn-consent";
  } else if (targetViewId === "view-wallet") {
    activeTabId = "tab-btn-wallet";
  } else if (targetViewId === "view-lay-summaries") {
    activeTabId = "tab-btn-lay-summaries";
  }

  if (activeTabId) {
    const tab = document.getElementById(activeTabId);
    if (tab) {
      tab.classList.add("active");
      const btn = tab.querySelector("button");
      if (btn) {
        btn.setAttribute("aria-selected", "true");
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
