// View switching. `state.currentView` (the ui store) is the source of truth the
// shell renders from; the router follows it into the URL and back (see
// src/router/index.js).
import { state } from "../stores/state.js";
import { loadConsentDetails } from "./consent.js";

// Activates a view: state (which the router follows into the URL), the
// `.active` classes, the tab bar's aria-selected, and loading the consent form
// when the consent view opens.
export function showView(viewId) {
  state.currentView = viewId;

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

  const targetView = document.getElementById(viewId);
  if (targetView) targetView.classList.add("active");

  // Highlight matching sidebar/navbar tab
  let activeTabId = null;
  if (viewId === "view-tasks" || viewId === "view-questionnaire") {
    activeTabId = "tab-btn-tasks";
  } else if (viewId === "view-compliance") {
    activeTabId = "tab-btn-compliance";
  } else if (viewId === "view-inbox") {
    activeTabId = "tab-btn-inbox";
  } else if (viewId === "view-consent") {
    activeTabId = "tab-btn-consent";
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

  if (viewId === "view-consent" && typeof loadConsentDetails === "function") {
    loadConsentDetails();
  }
}
