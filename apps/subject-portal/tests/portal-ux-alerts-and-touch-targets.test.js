/**
 * @req: PRD-PAT-001 PRD-PAT-002 PRD-PAY-006
 * Verifies zero blocking native alerts across questionnaire, consent, signature, and sync-queue flows,
 * .btn-start-task busy guard during instrument fetch, shared currency formatting, and 48px touch targets.
 */

import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, nextTick } from "vue";
import App from "../src/App.vue";
import {
  state,
  renderTasks,
  startQuestionnaire,
  submitConsentAnswers,
  verifyAndSubmitSignature,
  formatWalletCurrency,
  formatSignedWalletCurrency,
} from "../src/index.js";
import { requestQuestionnaireSignature } from "../src/features/questionnaire.js";
import { pinia } from "../src/stores/pinia.js";
import {
  initSessionKey,
  clearSessionKey,
  clearAllSubmissions,
  saveAssignmentsToDB,
} from "../src/sync-queue.js";
import {
  bindDelegatedClinicalWidgets,
  createClinicalInput,
  createClinicalRadioGrid,
  createClinicalSlider,
} from "../src/widgets/clinical-fields.js";

describe("Non-blocking UX notifications, task busy guard, and 48px touch targets", () => {
  let activeApp = null;
  let alertSpy = null;

  beforeEach(async () => {
    alertSpy = vi.spyOn(window, "alert").mockImplementation(() => {});
    state.session.userId = "subject_ux_001";
    state.session.token = "ux_session_token";
    state.session.isOfflineMode = false;
    state.session.isDemoMode = false;
    state.assignments = [];
    state.instruments = {};
    state.loadingTaskId = null;
    state.taskActionError = null;
    state.activeQuestionnaire = null;

    await initSessionKey("ux-passphrase", "subject_ux_001");
    await clearAllSubmissions();

    document.body.innerHTML = `<div id="app"></div>`;
    activeApp = createApp(App);
    activeApp.use(pinia);
    activeApp.mount("#app");
    await nextTick();
  });

  afterEach(async () => {
    expect(alertSpy).not.toHaveBeenCalled();
    if (activeApp) {
      activeApp.unmount();
      activeApp = null;
    }
    clearSessionKey("subject_ux_001");
    await clearAllSubmissions();
    vi.restoreAllMocks();
  });

  it("formats wallet currencies accurately across USD, EUR, GBP, and fallback codes", () => {
    expect(formatWalletCurrency(50, "EUR")).toBe("€50.00");
    expect(formatWalletCurrency(50, "USD")).toBe("$50.00");
    expect(formatWalletCurrency(50, "GBP")).toBe("£50.00");
    expect(formatWalletCurrency(12.5, "invalid")).toBe("$12.50");
    expect(formatSignedWalletCurrency(25, "EUR")).toBe("+€25.00");
    expect(formatSignedWalletCurrency(-15, "EUR")).toBe("-€15.00");
  });

  it("disables .btn-start-task with busy text while loading instrument and surfaces non-blocking alert banner on failure", async () => {
    state.currentView = "view-tasks";
    state.assignments = [
      {
        id: "assign_slow_01",
        instrument_id: "inst_slow_01",
        instrument_name: "Slow Daily Pain Survey",
        status: "PENDING",
        due_at: "2026-12-01T12:00:00Z",
      },
    ];
    renderTasks();
    await nextTick();

    let resolveFetch;
    const fetchPromise = new Promise((resolve) => {
      resolveFetch = resolve;
    });
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockImplementation(() => fetchPromise);

    const startBtn = document.querySelector(
      "#task-card-assign_slow_01 .btn-start-task"
    );
    expect(startBtn).not.toBeNull();
    expect(startBtn.disabled).toBe(false);

    // Click twice rapidly -> second click must be ignored while loading
    startBtn.click();
    startBtn.click();

    expect(startBtn.disabled).toBe(true);
    expect(startBtn.getAttribute("aria-busy")).toBe("true");
    expect(startBtn.textContent).toBe("Loading Survey...");
    expect(fetchSpy).toHaveBeenCalledTimes(1);

    // Resolve with 503 failure -> surfaces inline role="alert" banner + toast, zero window.alert
    resolveFetch({
      ok: false,
      status: 503,
      json: async () => ({ detail: "Instrument catalog offline" }),
    });

    await new Promise((r) => setTimeout(r, 50));
    await nextTick();

    const taskErrBanner = document.getElementById("tasks-action-error");
    expect(taskErrBanner).not.toBeNull();
    expect(taskErrBanner.getAttribute("role")).toBe("alert");
    expect(taskErrBanner.textContent).toContain(
      "Failed to retrieve questionnaire definition"
    );
    expect(document.getElementById("toast-container")?.textContent).toContain(
      "Failed to retrieve questionnaire definition"
    );
    fetchSpy.mockRestore();
  });

  it("surfaces inline role=alert banners and toasts instead of native alerts across questionnaire, consent, signature, and IndexedDB errors", async () => {
    // 1. Questionnaire invalid form submit -> inline #questionnaire-form-error + toast
    state.assignments = [
      {
        id: "assign_req_01",
        instrument_id: "inst_req_01",
        status: "PENDING",
      },
    ];
    state.instruments = {
      inst_req_01: {
        id: "inst_req_01",
        name: "Vital Diary",
        description: "Required fields check",
        items: {
          q_temp: { label: "Body Temp", type: "number", required: true },
        },
      },
    };
    await startQuestionnaire("assign_req_01");
    await nextTick();

    requestQuestionnaireSignature();
    const formErr = document.getElementById("questionnaire-form-error");
    expect(formErr.style.display).toBe("block");
    expect(formErr.getAttribute("role")).toBe("alert");
    expect(formErr.textContent).toContain(
      "Please fix all form errors before signing."
    );

    // 2. Consent incomplete comprehension check -> #comprehension-status-banner with role="alert"
    state.consentCheck = {
      questions: [
        {
          id: "cq_1",
          text: "May you withdraw at any time?",
          options: ["Yes", "No"],
        },
      ],
      expected_answers: { cq_1: "Yes" },
      threshold_policy: { min_correct: 1 },
    };
    await submitConsentAnswers();
    const compBanner = document.getElementById("comprehension-status-banner");
    expect(compBanner.style.display).toBe("block");
    expect(compBanner.getAttribute("role")).toBe("alert");
    expect(compBanner.textContent).toContain(
      "Please answer all comprehension questions"
    );

    // 3. Signature modal missing credentials & custom signing reason ("Other") -> #modal-error-banner or toast, zero window.alert
    document.getElementById("sign-username").value = "";
    document.getElementById("sign-password").value = "";
    await verifyAndSubmitSignature();
    const modalErr = document.getElementById("modal-error-banner");
    expect(modalErr.style.display).toBe("block");
    expect(modalErr.getAttribute("role")).toBe("alert");

    document.getElementById("sign-username").value = "subject_ux_001";
    document.getElementById("sign-password").value = "246810";
    const signReasonSelect = document.getElementById("sign-reason");
    signReasonSelect.innerHTML = `<option value="Other">Other</option>`;
    signReasonSelect.value = "Other";
    document.getElementById("sign-reason-custom").value = "   ";
    await verifyAndSubmitSignature();
    expect(modalErr.style.display).toBe("block");
    expect(modalErr.getAttribute("role")).toBe("alert");
    expect(modalErr.textContent.toLowerCase()).toContain("reason");

    // Fallback when #modal-error-banner is absent -> error toast with role="alert"
    modalErr.remove();
    await verifyAndSubmitSignature();
    const reasonToast = document.getElementById("toast-container");
    expect(reasonToast).not.toBeNull();
    expect(reasonToast.classList.contains("toast-error")).toBe(true);
    expect(reasonToast.getAttribute("role")).toBe("alert");

    // 4. IndexedDB open failure in sync-queue -> toast-error with role="alert", zero window.alert
    const openSpy = vi
      .spyOn(globalThis.indexedDB, "open")
      .mockImplementationOnce(() => {
        throw new Error("Simulated storage quota failure");
      });
    await saveAssignmentsToDB([{ id: "a1" }]);
    const toast = document.getElementById("toast-container");
    expect(toast).not.toBeNull();
    expect(toast.classList.contains("toast-error")).toBe(true);
    expect(toast.getAttribute("role")).toBe("alert");
    openSpy.mockRestore();
  });

  it("renders 48px touch-target tokens and label-wrapped .radio-option items without raw hex colors", () => {
    const sliderHtml = createClinicalSlider(
      "pain_vas",
      "Pain VAS",
      3,
      null,
      12,
      {
        min: 0,
        max: 10,
      }
    );
    expect(sliderHtml).toContain("min-height: var(--touch-target-min)");
    expect(sliderHtml).not.toMatch(/#[0-9a-fA-F]{3,6}/);

    const radioHtml = createClinicalRadioGrid("q_choice", "Select one", [
      { value: "yes", label: "Yes" },
      { value: "no", label: "No" },
    ]);
    expect(radioHtml).toContain(
      '<label class="radio-option" for="q_choice_option_0">'
    );
    expect(radioHtml).not.toMatch(/#[0-9a-fA-F]{3,6}/);

    const langSelect = document.getElementById("consent-lang-selector");
    expect(langSelect?.style.minHeight).toBe("var(--touch-target-min)");
  });

  it("emits zero inline on*= event attributes and updates sliders and query panels via delegated listeners", () => {
    const sliderHtml = createClinicalSlider(
      "pain_scale",
      "Pain Scale",
      2,
      null,
      12,
      {
        min: 0,
        max: 10,
      }
    );
    const inputHtml = createClinicalInput("vssbp", "Systolic BP", "");
    const radioHtml = createClinicalRadioGrid("comp_q1", "Voluntary?", [
      "Yes",
      "No",
    ]);

    for (const html of [sliderHtml, inputHtml, radioHtml]) {
      expect(html).not.toMatch(/\bon[a-z]+\s*=/i);
    }

    const host = document.createElement("div");
    document.body.appendChild(host);
    const onFieldInput = vi.fn();

    // Bind twice to verify WeakMap idempotency (no stacked toggle handlers)
    host.innerHTML = `${sliderHtml}${radioHtml}`;
    bindDelegatedClinicalWidgets(host, { onFieldInput });
    bindDelegatedClinicalWidgets(host, { onFieldInput });

    const slider = host.querySelector("#pain_scale");
    const badge = host.querySelector("#pain_scale_display");
    slider.value = "7";
    slider.dispatchEvent(new Event("input", { bubbles: true }));
    expect(slider.getAttribute("data-touched")).toBe("true");
    expect(badge.textContent.trim()).toBe("Value: 7");
    expect(onFieldInput).toHaveBeenCalledTimes(1);

    const flag = host.querySelector("#query-flag-pain_scale");
    const panel = host.querySelector("#query-panel-pain_scale");
    const closeBtn = panel.querySelector(".btn-close-panel");

    expect(panel.style.display).toBe("none");
    expect(flag.getAttribute("aria-expanded")).toBe("false");

    flag.click();
    expect(panel.style.display).toBe("block");
    expect(flag.getAttribute("aria-expanded")).toBe("true");

    closeBtn.click();
    expect(panel.style.display).toBe("none");
    expect(flag.getAttribute("aria-expanded")).toBe("false");

    host.remove();
  });
});
