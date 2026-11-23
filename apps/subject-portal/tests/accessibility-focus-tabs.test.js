import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import { getFocusableElements } from "ui";
import {
  initializeApp,
  unmountApp,
  state,
  showView,
  startQuestionnaire,
  validateActiveQuestionnaire,
  confirmCancelQuestionnaire,
  verifyAndSubmitSignature,
  openSignatureModal,
  openReconsentModal,
} from "../src/index.js";
import { tabs } from "../src/router/routes.js";
import { useSessionStore } from "../src/stores/session.js";
import {
  initSessionKey,
  clearSessionKey,
  getDraft,
  clearAllSubmissions,
} from "../src/sync-queue.js";

const settle = async () => {
  await nextTick();
  await new Promise((resolve) => setTimeout(resolve, 25));
  await nextTick();
};

describe("Subject Portal Focus Traps, Closed Drawer Inertness, and 6-Tab Navigation", () => {
  beforeEach(async () => {
    window.__MOCK_TEST_ENV__ = true;
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () => Promise.resolve([]),
    });
    document.body.innerHTML = `<div id="app"></div>`;
    state.currentView = "view-tasks";
    state.isSyncDrawerOpen = false;
    state.signatureModalOpen = false;
    state.reconsentModalOpen = false;
    await initializeApp();
    await settle();
  });

  afterEach(() => {
    const session = useSessionStore();
    session.pinSetup.isOpen = false;
    session.pinUnlock.isOpen = false;
    state.isSyncDrawerOpen = false;
    state.signatureModalOpen = false;
    state.reconsentModalOpen = false;
    unmountApp();
  });

  it("hides closed SyncDrawer and scrim from keyboard tabbing and assistive technology, and traps/restores focus when opened", async () => {
    const trigger = document.getElementById("btn-global-sync-drawer");
    const drawer = document.querySelector(".global-drawer");
    const overlay = document.querySelector(".global-drawer-overlay");

    expect(drawer.hasAttribute("inert")).toBe(true);
    expect(drawer.getAttribute("aria-hidden")).toBe("true");
    expect(drawer.getAttribute("aria-modal")).toBeNull();
    expect(overlay.hasAttribute("inert")).toBe(true);
    expect(overlay.getAttribute("aria-hidden")).toBe("true");
    expect(getFocusableElements(drawer)).toEqual([]);

    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    trigger.click();
    await settle();

    expect(state.isSyncDrawerOpen).toBe(true);
    expect(drawer.hasAttribute("inert")).toBe(false);
    expect(drawer.getAttribute("aria-hidden")).toBeNull();
    expect(drawer.getAttribute("aria-modal")).toBe("true");
    expect(drawer.contains(document.activeElement)).toBe(true);

    const focusables = getFocusableElements(drawer);
    expect(focusables.length).toBeGreaterThanOrEqual(2);
    const firstEl = focusables[0];
    const lastEl = focusables[focusables.length - 1];

    lastEl.focus();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(firstEl);

    firstEl.focus();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        shiftKey: true,
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(lastEl);

    // Escape closes the drawer and restores focus to the trigger button
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();

    expect(state.isSyncDrawerOpen).toBe(false);
    expect(drawer.hasAttribute("inert")).toBe(true);
    expect(document.activeElement).toBe(trigger);
  });

  it("traps focus inside mandatory PinSetupModal and PinUnlockModal while blocking Escape dismissal", async () => {
    const session = useSessionStore();

    session.pinSetup.isOpen = true;
    await settle();

    const setupModal = document.getElementById("portal-pin-setup-modal");
    expect(setupModal).not.toBeNull();
    expect(setupModal.contains(document.activeElement)).toBe(true);

    const setupEsc = new KeyboardEvent("keydown", {
      key: "Escape",
      bubbles: true,
      cancelable: true,
    });
    document.dispatchEvent(setupEsc);
    await settle();
    expect(setupEsc.defaultPrevented).toBe(true);
    expect(session.pinSetup.isOpen).toBe(true);

    session.pinSetup.isOpen = false;
    await settle();

    session.pinUnlock.isOpen = true;
    await settle();

    const unlockModal = document.getElementById("portal-pin-unlock-modal");
    expect(unlockModal).not.toBeNull();
    expect(unlockModal.contains(document.activeElement)).toBe(true);

    const unlockEsc = new KeyboardEvent("keydown", {
      key: "Escape",
      bubbles: true,
      cancelable: true,
    });
    document.dispatchEvent(unlockEsc);
    await settle();
    expect(unlockEsc.defaultPrevented).toBe(true);
    expect(session.pinUnlock.isOpen).toBe(true);
  });

  it("clears typed 21 CFR Part 11 credentials immediately when SignatureModal or ReconsentModal is dismissed with Escape", async () => {
    const trigger = document.getElementById("btn-global-sync-drawer");
    trigger.focus();

    openSignatureModal({ vssbp: "120" }, false);
    await settle();

    const signModal = document.getElementById("portal-sign-modal");
    const signPw = document.getElementById("sign-password");
    expect(state.signatureModalOpen).toBe(true);
    expect(signModal.contains(document.activeElement)).toBe(true);

    signPw.value = "SuperSecretPin123!";
    signPw.dispatchEvent(new Event("input", { bubbles: true }));

    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();

    expect(state.signatureModalOpen).toBe(false);
    expect(signPw.value).toBe("");
    expect(document.activeElement).toBe(trigger);

    // Verify ReconsentModal Escape credential wiping as well
    trigger.focus();
    openReconsentModal();
    await settle();

    const reconsentPw = document.getElementById("reconsent-sign-password");
    expect(state.reconsentModalOpen).toBe(true);
    state.reconsentForm.password = "AmendmentSecret456!"; // pragma: allowlist secret
    await nextTick();
    expect(reconsentPw.value).toBe("AmendmentSecret456!");

    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();

    expect(state.reconsentModalOpen).toBe(false);
    expect(state.reconsentForm.password).toBe("");
    expect(document.activeElement).toBe(trigger);

    // Also verify standalone DOM #portal-sign-modal traps Tab and Escape via trapTabFocus when Vue shell is unmounted
    unmountApp();
    document.body.innerHTML = `
      <button id="standalone-trigger" type="button">Sign</button>
      <div id="portal-sign-modal" style="display: none;">
        <select id="sign-reason"></select>
        <textarea id="sign-reason-custom"></textarea>
        <input id="sign-username" type="text" />
        <input id="sign-password" type="password" />
        <button id="btn-modal-cancel" type="button">Cancel</button>
        <button id="btn-modal-sign" type="submit">Confirm</button>
      </div>
    `;
    const standaloneTrigger = document.getElementById("standalone-trigger");
    standaloneTrigger.focus();
    openSignatureModal("epro");

    const standaloneModal = document.getElementById("portal-sign-modal");
    const standaloneFocusables = getFocusableElements(standaloneModal);
    expect(document.activeElement).toBe(standaloneFocusables[0]);

    const lastStandalone =
      standaloneFocusables[standaloneFocusables.length - 1];
    lastStandalone.focus();
    window.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(standaloneFocusables[0]);

    window.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(state.signatureModalOpen).toBe(false);
    expect(document.activeElement).toBe(standaloneTrigger);
  });

  it("traps focus inside QuestionnaireView cancel confirmation dialog and restores focus on Escape", async () => {
    state.assignments = [
      {
        id: "assign_cancel_test",
        subject_id: "subj_01",
        instrument_id: "inst_cancel_test",
        instrument_name: "Daily Symptom Diary",
        status: "PENDING",
      },
    ];
    state.instruments = {
      inst_cancel_test: {
        id: "inst_cancel_test",
        name: "Daily Symptom Diary",
        description: "Record symptoms",
        items: {
          symptom_score: {
            label: "Symptom Score",
            type: "numeric",
            required: true,
            min: 0,
            max: 10,
          },
        },
      },
    };
    state.session.isDemoMode = true;
    state.activeQuestionnaire = {
      assignment: state.assignments[0],
      instrument: state.instruments.inst_cancel_test,
      answers: { symptom_score: 4 },
    };
    showView("view-questionnaire");
    await settle();

    const cancelBtn = document.getElementById("btn-cancel-questionnaire");
    expect(cancelBtn).not.toBeNull();
    cancelBtn.focus();
    cancelBtn.click();
    await settle();

    const dialog = document.getElementById("questionnaire-cancel-confirm");
    const keepBtn = document.getElementById("btn-cancel-keep-editing");
    const discardBtn = document.getElementById("btn-cancel-confirm-discard");
    expect(state.questionnaireCancelConfirmOpen).toBe(true);
    expect(dialog).not.toBeNull();
    expect(document.activeElement).toBe(keepBtn);

    // Shift+Tab wraps from Keep Editing to Discard Draft & Leave
    dialog.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        shiftKey: true,
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(discardBtn);

    // Forward Tab wraps back to Keep Editing
    dialog.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Tab",
        bubbles: true,
        cancelable: true,
      })
    );
    expect(document.activeElement).toBe(keepBtn);

    // Escape dismisses confirmation dialog without discarding active questionnaire and restores focus
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();

    expect(state.questionnaireCancelConfirmOpen).toBe(false);
    expect(state.activeQuestionnaire).not.toBeNull();
    expect(document.activeElement).toBe(cancelBtn);
    state.activeQuestionnaire = null;
  });

  it("drives participant portal tabs from a single state source across unauthenticated, wallet-only signed-in, and 6-tab demo modes", async () => {
    expect(tabs).toHaveLength(6);

    // 1a. Unauthenticated non-demo mode hides all 6 tabs and uses role="region" + aria-label with zero dangling aria-labelledby
    state.session.isDemoMode = false;
    state.session.token = null;
    state.session.userId = null;
    await settle();
    expect(document.querySelectorAll('.nav-tabs [role="tab"]')).toHaveLength(0);

    for (const [viewId, expectedLabel] of [
      ["view-tasks", "My Daily Questionnaires"],
      ["view-compliance", "My Compliance Overview"],
      ["view-inbox", "My Notification Inbox"],
      ["view-consent", "My Informed Consent Review"],
      ["view-wallet", "Participant Financial Wallet"],
      ["view-lay-summaries", "Plain-Language Clinical Trial Results"],
    ]) {
      const panel = document.getElementById(viewId);
      expect(panel.getAttribute("role")).toBe("region");
      expect(panel.getAttribute("aria-label")).toBe(expectedLabel);
      expect(panel.hasAttribute("aria-labelledby")).toBe(false);
    }

    for (const labelledEl of document.querySelectorAll("[aria-labelledby]")) {
      const refIds = labelledEl
        .getAttribute("aria-labelledby")
        .trim()
        .split(/\s+/);
      for (const refId of refIds) {
        expect(document.getElementById(refId)).not.toBeNull();
      }
    }

    // 1b. Signed-in non-demo mode renders only the live Wallet tab while keeping the 5 unbacked tabs hidden
    state.session.token = "jwt_portal_test"; // pragma: allowlist secret
    state.session.userId = "subject_001";
    state.session.isOfflineMode = false;
    showView("view-wallet");
    await settle();

    const signedInTabs = document.querySelectorAll('.nav-tabs [role="tab"]');
    expect(signedInTabs).toHaveLength(1);
    expect(signedInTabs[0].id).toBe("btn-tab-btn-wallet");
    const walletPanelLive = document.getElementById("view-wallet");
    expect(walletPanelLive.getAttribute("role")).toBe("tabpanel");
    expect(walletPanelLive.getAttribute("aria-labelledby")).toBe(
      "btn-tab-btn-wallet"
    );

    for (const labelledEl of document.querySelectorAll("[aria-labelledby]")) {
      const refIds = labelledEl
        .getAttribute("aria-labelledby")
        .trim()
        .split(/\s+/);
      for (const refId of refIds) {
        expect(document.getElementById(refId)).not.toBeNull();
      }
    }

    // 2. Demo mode renders all 6 tabs with bidirectional ARIA links and roving tabindex navigation
    state.session.token = null;
    state.session.userId = null;
    state.session.isDemoMode = true;
    await settle();

    const tabButtons = Array.from(
      document.querySelectorAll('.nav-tabs [role="tab"]')
    );
    expect(tabButtons).toHaveLength(6);

    // Check every tab <-> tabpanel bidirectional relationship and single-source selection
    for (const tab of tabs) {
      showView(tab.viewId);
      await settle();

      for (const candidate of tabs) {
        const btn = document.getElementById(candidate.buttonId);
        const panel = document.getElementById(candidate.viewId);
        expect(btn).not.toBeNull();
        expect(panel).not.toBeNull();
        expect(btn.getAttribute("aria-controls")).toBe(candidate.viewId);
        expect(panel.getAttribute("aria-labelledby")).toBe(candidate.buttonId);
        expect(panel.getAttribute("role")).toBe("tabpanel");

        const isSelected = candidate.id === tab.id;
        expect(btn.getAttribute("aria-selected")).toBe(String(isSelected));
        expect(btn.getAttribute("tabindex")).toBe(isSelected ? "0" : "-1");
        expect(panel.hasAttribute("hidden")).toBe(!isSelected);
        expect(panel.hasAttribute("inert")).toBe(!isSelected);
        if (!isSelected) {
          expect(getFocusableElements(panel)).toEqual([]);
        }
      }

      // Non-tab portal views (questionnaire, unavailable) must also remain hidden + inert
      for (const extraViewId of ["view-questionnaire", "view-unavailable"]) {
        const extraPanel = document.getElementById(extraViewId);
        expect(extraPanel.hasAttribute("hidden")).toBe(true);
        expect(extraPanel.hasAttribute("inert")).toBe(true);
        expect(getFocusableElements(extraPanel)).toEqual([]);
      }
    }

    // Verify ArrowLeft, ArrowRight, End, Home navigation across all 6 tabs
    showView("view-tasks");
    await settle();

    const firstTab = document.getElementById("btn-tab-btn-tasks");
    const lastTab = document.getElementById("btn-tab-btn-lay-summaries");
    firstTab.focus();

    // ArrowLeft from first wraps to 6th tab (Trial Results / Lay Summaries)
    firstTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "ArrowLeft",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-lay-summaries");
    expect(document.activeElement).toBe(lastTab);
    expect(lastTab.getAttribute("aria-selected")).toBe("true");

    // ArrowRight from 6th tab wraps back to 1st tab (Tasks)
    lastTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "ArrowRight",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-tasks");
    expect(document.activeElement).toBe(firstTab);

    // End jumps to 6th tab; Home jumps back to 1st tab
    firstTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "End",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-lay-summaries");
    expect(document.activeElement).toBe(lastTab);

    lastTab.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Home",
        bubbles: true,
        cancelable: true,
      })
    );
    await settle();
    expect(state.currentView).toBe("view-tasks");
    expect(document.activeElement).toBe(firstTab);

    state.session.isDemoMode = false;
  });

  it("wires view-questionnaire as an accessible tabpanel with standalone fallback and flushes drafts immediately on tab exits", async () => {
    unmountApp();
    document.body.innerHTML = `<div id="app"></div>`;
    state.session.isDemoMode = true;
    state.session.isOfflineMode = true;
    state.session.userId = "subject_001";
    await initializeApp();
    await initSessionKey("tab-flush-secret", "subject_001");
    await clearAllSubmissions();
    await settle();

    await startQuestionnaire("assign_01");
    await settle();

    const tasksTab = document.getElementById("btn-tab-btn-tasks");
    const complianceTab = document.getElementById("btn-tab-btn-compliance");
    const questionnairePanel = document.getElementById("view-questionnaire");
    const tasksPanel = document.getElementById("view-tasks");

    expect(questionnairePanel.getAttribute("role")).toBe("tabpanel");
    expect(questionnairePanel.getAttribute("tabindex")).toBe("0");
    expect(questionnairePanel.getAttribute("aria-labelledby")).toBe(
      "btn-tab-btn-tasks"
    );
    expect(questionnairePanel.hasAttribute("hidden")).toBe(false);
    expect(questionnairePanel.hasAttribute("inert")).toBe(false);
    expect(tasksTab.getAttribute("aria-selected")).toBe("true");
    expect(tasksTab.getAttribute("aria-controls")).toBe("view-questionnaire");
    expect(tasksPanel.hasAttribute("hidden")).toBe(true);
    expect(tasksPanel.hasAttribute("inert")).toBe(true);

    // Untouched required VAS slider blocks validation with inline error and form banner
    document.getElementById("vssbp").value = "122";
    document.getElementById("vsdpb").value = "78";
    document.getElementById("vshr").value = "68";
    document.getElementById("has_symptoms_option_1").checked = true;
    document.getElementById("btn-submit-questionnaire").click();
    expect(state.signatureModalOpen).toBe(false);
    expect(
      document
        .getElementById("field-container-pain_scale")
        .classList.contains("has-error")
    ).toBe(true);
    expect(
      document.getElementById("questionnaire-form-error")?.textContent
    ).toContain("Please fix all form errors before signing.");

    // Deliberate 0 selection via pointerdown marks slider touched with Value: 0
    const painSlider = document.getElementById("pain_scale");
    painSlider.dispatchEvent(new Event("pointerdown", { bubbles: true }));
    expect(painSlider.getAttribute("data-touched")).toBe("true");
    expect(
      document.getElementById("pain_scale_display")?.textContent
    ).toContain("Value: 0");
    expect(validateActiveQuestionnaire()).toBe(true);

    // Switching tabs via TabBar flushes the encrypted draft immediately without waiting 1500ms
    complianceTab.click();
    await settle();

    expect(state.currentView).toBe("view-compliance");
    expect(state.activeQuestionnaire).toBeNull();
    expect(tasksTab.getAttribute("aria-controls")).toBe("view-tasks");

    const flushedDraft = await getDraft("assign_01");
    expect(flushedDraft).not.toBeNull();
    expect(flushedDraft.vssbp).toBe("122");
    expect(flushedDraft.pain_scale).toBe("0");

    tasksTab.click();
    await settle();
    expect(
      document.querySelector("#task-card-assign_01 .btn-start-task")
        ?.textContent
    ).toContain("Resume Survey");

    // Reopening restores touched VAS state; explicit discard does NOT re-save draft on leave
    await startQuestionnaire("assign_01");
    await settle();
    expect(
      document.getElementById("pain_scale")?.getAttribute("data-touched")
    ).toBe("true");
    expect(
      document.getElementById("pain_scale_display")?.textContent
    ).toContain("Value: 0");

    await confirmCancelQuestionnaire();
    await settle();
    expect(await getDraft("assign_01")).toBeNull();

    // Completing & signing submission also leaves view-questionnaire without re-saving a draft
    await startQuestionnaire("assign_01");
    await settle();
    document.getElementById("vssbp").value = "120";
    document.getElementById("vsdpb").value = "80";
    document.getElementById("vshr").value = "70";
    document.getElementById("has_symptoms_option_1").checked = true;
    const signedSlider = document.getElementById("pain_scale");
    signedSlider.dispatchEvent(
      new KeyboardEvent("keydown", { key: "ArrowLeft", bubbles: true })
    );
    expect(validateActiveQuestionnaire()).toBe(true);
    document.getElementById("sign-username").value = "subject_001";
    document.getElementById("sign-password").value = "pass123";
    await verifyAndSubmitSignature();
    await settle();
    expect(await getDraft("assign_01")).toBeNull();

    // Standalone JSDOM fallback when #btn-tab-btn-tasks is absent
    unmountApp();
    document.body.innerHTML = `
      <section id="view-questionnaire" class="portal-view">
        <h2 id="questionnaire-title">Standalone Survey</h2>
      </section>
    `;
    showView("view-questionnaire");
    expect(
      document
        .getElementById("view-questionnaire")
        .getAttribute("aria-labelledby")
    ).toBe("questionnaire-title");

    clearSessionKey("subject_001");
    await clearAllSubmissions();
  });
});
