// Event bindings for the controls the feature modules own, and the tab
// list's ARIA roles and arrow-key navigation. Called once per initializeApp().
import { showView } from "../features/navigation.js";
import { retryCompliance } from "../features/compliance.js";
import {
  loadConsentDetails,
  submitConsentAnswers,
} from "../features/consent.js";
import { retryInbox } from "../features/inbox.js";
import {
  leaveQuestionnaire,
  requestQuestionnaireSignature,
} from "../features/questionnaire.js";
import {
  closeSignatureModal,
  openSignatureModal,
  verifyAndSubmitSignature,
} from "../features/signature.js";
import { syncOfflineQueue } from "../features/sync.js";
import { retryTasks } from "../features/tasks.js";

export function bindPortalControls() {
  // Attach "Sync Now" button listener
  const btnSyncNow = document.getElementById("btn-sync-now");
  if (btnSyncNow) {
    btnSyncNow.addEventListener("click", () => {
      syncOfflineQueue(true, { autoDisburse: true });
    });
  }

  // Attach Global Navigation listeners
  const btnTasks = document.getElementById("tab-btn-tasks");
  const btnCompliance = document.getElementById("tab-btn-compliance");
  const btnInbox = document.getElementById("tab-btn-inbox");
  const btnConsent = document.getElementById("tab-btn-consent");

  if (btnTasks)
    btnTasks.addEventListener("click", () => showView("view-tasks"));
  if (btnCompliance)
    btnCompliance.addEventListener("click", () => showView("view-compliance"));
  if (btnInbox)
    btnInbox.addEventListener("click", () => showView("view-inbox"));
  if (btnConsent)
    btnConsent.addEventListener("click", () => showView("view-consent"));

  const btnBack = document.getElementById("btn-back-to-tasks");
  if (btnBack) btnBack.addEventListener("click", leaveQuestionnaire);

  const btnCancel = document.getElementById("btn-cancel-questionnaire");
  if (btnCancel) btnCancel.addEventListener("click", leaveQuestionnaire);

  const btnSubmit = document.getElementById("btn-submit-questionnaire");
  if (btnSubmit)
    btnSubmit.addEventListener("click", requestQuestionnaireSignature);

  // Modal actions
  const btnModalCancel = document.getElementById("btn-modal-cancel");
  if (btnModalCancel)
    btnModalCancel.addEventListener("click", closeSignatureModal);

  const btnModalSign = document.getElementById("btn-modal-sign");
  if (btnModalSign)
    btnModalSign.addEventListener("click", verifyAndSubmitSignature);

  // eConsent visual element binding
  const langSelector = document.getElementById("consent-lang-selector");
  if (langSelector) {
    langSelector.addEventListener("change", () => {
      loadConsentDetails();
    });
  }

  const btnRetryConsent = document.getElementById("btn-retry-consent");
  if (btnRetryConsent) {
    btnRetryConsent.addEventListener("click", () => {
      loadConsentDetails();
    });
  }

  const btnSubmitAnswers = document.getElementById(
    "btn-submit-consent-answers"
  );
  if (btnSubmitAnswers) {
    btnSubmitAnswers.addEventListener("click", () => {
      submitConsentAnswers();
    });
  }

  const btnTriggerSign = document.getElementById("btn-trigger-consent-sign");
  if (btnTriggerSign) {
    btnTriggerSign.addEventListener("click", () => {
      openSignatureModal("consent");
    });
  }

  // Retry Handlers
  const btnRetryTasks = document.getElementById("btn-retry-tasks");
  if (btnRetryTasks) {
    btnRetryTasks.addEventListener("click", retryTasks);
  }

  const btnRetryInbox = document.getElementById("btn-retry-inbox");
  if (btnRetryInbox) {
    btnRetryInbox.addEventListener("click", retryInbox);
  }

  const btnRetryCompliance = document.getElementById("btn-retry-compliance");
  if (btnRetryCompliance) {
    btnRetryCompliance.addEventListener("click", retryCompliance);
  }
}

export function setupTabListAria() {
  // Setup semantic ARIA roles, states, and arrow key navigation for the tab list
  const navTabs = document.querySelector(".nav-tabs");
  if (navTabs) {
    navTabs.setAttribute("role", "tablist");
    const tabItems = Array.from(navTabs.querySelectorAll(".nav-item"));
    tabItems.forEach((item) => {
      item.setAttribute("role", "presentation");
      const btn = item.querySelector("button");
      if (btn) {
        btn.setAttribute("role", "tab");
        if (!btn.id) {
          btn.id = "btn-" + item.id;
        }
        let controls = "";
        if (item.id === "tab-btn-tasks") controls = "view-tasks";
        else if (item.id === "tab-btn-compliance") controls = "view-compliance";
        else if (item.id === "tab-btn-inbox") controls = "view-inbox";
        else if (item.id === "tab-btn-consent") controls = "view-consent";
        btn.setAttribute("aria-controls", controls);

        const isSelected = item.classList.contains("active");
        btn.setAttribute("aria-selected", isSelected ? "true" : "false");
      }
    });

    document.querySelectorAll(".portal-view").forEach((v) => {
      v.setAttribute("role", "tabpanel");
      let btnId = "";
      if (v.id === "view-tasks") btnId = "btn-tab-btn-tasks";
      else if (v.id === "view-compliance") btnId = "btn-tab-btn-compliance";
      else if (v.id === "view-inbox") btnId = "btn-tab-btn-inbox";
      else if (v.id === "view-consent") btnId = "btn-tab-btn-consent";
      if (btnId) {
        v.setAttribute("aria-labelledby", btnId);
      }
    });

    // Remove any existing keydown listener to avoid duplicates
    if (navTabs._keydownListener) {
      navTabs.removeEventListener("keydown", navTabs._keydownListener);
    }
    navTabs._keydownListener = (e) => {
      const activeElement = document.activeElement;
      if (
        !activeElement ||
        activeElement.tagName !== "BUTTON" ||
        activeElement.getAttribute("role") !== "tab"
      ) {
        return;
      }

      const buttons = Array.from(
        navTabs.querySelectorAll("button[role='tab']")
      );
      const currentIndex = buttons.indexOf(activeElement);
      if (currentIndex === -1) return;

      let nextIndex = currentIndex;
      if (e.key === "ArrowRight" || e.key === "ArrowDown") {
        nextIndex = (currentIndex + 1) % buttons.length;
        e.preventDefault();
      } else if (e.key === "ArrowLeft" || e.key === "ArrowUp") {
        nextIndex = (currentIndex - 1 + buttons.length) % buttons.length;
        e.preventDefault();
      } else if (e.key === "Home") {
        nextIndex = 0;
        e.preventDefault();
      } else if (e.key === "End") {
        nextIndex = buttons.length - 1;
        e.preventDefault();
      }

      if (nextIndex !== currentIndex) {
        const nextBtn = buttons[nextIndex];
        nextBtn.focus();
        nextBtn.click();
      }
    };
    navTabs.addEventListener("keydown", navTabs._keydownListener);
  }
}
