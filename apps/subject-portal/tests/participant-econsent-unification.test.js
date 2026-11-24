import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { nextTick } from "vue";
import {
  initializeApp,
  unmountApp,
  state,
  openSignatureModal,
  verifyAndSubmitSignature,
  openReconsentModal,
  submitReconsentSignature,
  loadConsentDetails,
  submitConsentAnswers,
  verifyBiometricLiveness,
  resolveSessionSignerUsername,
  hydrateSessionScopeFromClaims,
} from "../src/index.js";
import { isScreenAvailable } from "../src/router/availability.js";
import {
  getPortalRouter,
  resetPortalRouterForTests,
} from "../src/router/index.js";
import {
  getActiveUserId,
  initSessionKey,
  saveDraft,
  getDraft,
} from "../src/sync-queue.js";

function encodeFakeJwt(claims) {
  const header = btoa(JSON.stringify({ alg: "HS256", typ: "JWT" }))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
  return `${header}.${payload}.sig`;
}

const settle = async () => {
  await nextTick();
  await new Promise((resolve) => setTimeout(resolve, 25));
  await nextTick();
};

describe("Unified Participant eConsent APIs & Shared Signing Modals", () => {
  beforeEach(() => {
    window.__MOCK_TEST_ENV__ = true;
    localStorage.clear();
    sessionStorage.clear();
    unmountApp();
    resetPortalRouterForTests();
    document.body.innerHTML = `<div id="app"></div>`;
    state.session.isDemoMode = false;
    state.session.isOfflineMode = false;
    state.session.userId = null;
    state.session.username = null;
    state.session.usernameBoundToUserId = null;
    state.session.token = null;
    state.session.studyId = null;
    state.session.siteId = null;
    state.pendingReconsent = null;
    state.reconsentModalOpen = false;
    state.signatureModalOpen = false;
    state.consentContent = null;
    state.consentQuestions = [];
    state.consentComprehensionPassed = false;
    state.consentPassed = false;
    state.consentSigned = false;
  });

  afterEach(() => {
    state.pendingReconsent = null;
    state.reconsentModalOpen = false;
    state.signatureModalOpen = false;
    unmountApp();
    resetPortalRouterForTests();
    localStorage.clear();
    sessionStorage.clear();
  });

  it("unlocks /consent for authenticated non-demo sessions while redirecting unauthenticated visits to /unavailable and running startup pending re-consent checks", async () => {
    // Unauthenticated non-demo session cannot access /consent
    expect(isScreenAvailable("consent")).toBe(false);

    const oidcSub = "7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d";
    const jwt = encodeFakeJwt({
      sub: oidcSub,
      preferred_username: "subject.site101",
      study_ids: ["CADENCE-101"],
      site_id: "SITE-101",
    });

    state.session.userId = oidcSub;
    state.session.token = jwt;
    expect(isScreenAvailable("consent")).toBe(true);

    globalThis.fetch = vi.fn(async (url) => {
      const urlStr = String(url);
      if (urlStr.includes("/api/v1/econsent/reconsent/pending/CADENCE-101")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            study_id: "CADENCE-101",
            subject_pseudonym: oidcSub,
            pending_count: 1,
            pending_items: [
              {
                id: "req_amend_101",
                study_id: "CADENCE-101",
                site_id: "SITE-101",
                protocol_version: "2.0",
                new_version_index: 2,
                change_summary:
                  "Added optional Week 8 remote blood pressure check.",
              },
            ],
          }),
        };
      }
      return {
        ok: true,
        status: 200,
        json: async () => ({
          walletBalance: 50,
          currency: "USD",
          expenseClaims: [],
          transactions: [],
        }),
      };
    });

    window.history.replaceState({}, "", "/subject-portal/consent");
    await initializeApp();
    await settle();

    const router = getPortalRouter();
    expect(router.currentRoute.value.path).toBe("/consent");
    expect(state.currentView).toBe("view-consent");

    // Startup checkPendingReconsent ran outside demo mode with subject_pseudonym = OIDC sub and opened ReconsentModal
    const calledUrls = globalThis.fetch.mock.calls.map(([u]) => String(u));
    expect(
      calledUrls.some(
        (u) =>
          u.includes("/api/v1/econsent/reconsent/pending/CADENCE-101") &&
          u.includes(`subject_pseudonym=${encodeURIComponent(oidcSub)}`)
      )
    ).toBe(true);
    expect(state.pendingReconsent?.id).toBe("req_amend_101");
    expect(state.reconsentModalOpen).toBe(true);
    expect(state.reconsentForm.username).toBe("subject.site101");

    // Clearing auth token outside demo mode redirects /consent to /unavailable
    state.reconsentModalOpen = false;
    state.session.token = null;
    await settle();
    expect(router.currentRoute.value.path).toBe("/unavailable");
  });

  it("separates OIDC preferred_username (step-up signer) from OIDC sub (IndexedDB scope & subject_pseudonym) across initial consent and dynamic template versions", async () => {
    const oidcSub = "7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d";
    const preferredUsername = "subject.site101";
    const jwt = encodeFakeJwt({
      sub: oidcSub,
      preferred_username: preferredUsername,
      study_ids: ["CADENCE-101"],
      site_id: "SITE-101",
    });

    state.session.userId = oidcSub;
    state.session.token = jwt;
    hydrateSessionScopeFromClaims(null, jwt);

    const calls = [];
    globalThis.fetch = vi.fn(async (url, init = {}) => {
      const urlStr = String(url);
      const parsedBody = init?.body ? JSON.parse(init.body) : null;
      calls.push({
        url: urlStr,
        method: init?.method || "GET",
        body: parsedBody,
        headers: init?.headers || {},
      });

      if (urlStr.includes("/approved-content")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            template_id: "tmpl-cardio-09",
            template_name: "CADENCE-101 Informed Consent v3.0",
            version_index: 3,
            version_number: "3.0",
            language_code: "en",
            study_id: "CADENCE-101",
            clauses: [
              {
                clause_id: "sec-1",
                title: "Study Purpose",
                text: "Evaluate remote vitals.",
                version_index: 3,
              },
            ],
          }),
        };
      }
      if (
        urlStr.includes(
          "/api/v1/econsent/templates/tmpl-cardio-09/versions/3/comprehension-checks"
        )
      ) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            questions: [
              {
                id: "q_voluntary",
                text: "Is participation voluntary?",
                options: ["Yes", "No"],
              },
            ],
          }),
        };
      }
      if (
        urlStr.includes(
          "/api/v1/econsent/templates/tmpl-cardio-09/versions/3/submit-answers"
        )
      ) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            id: "comp-res-01",
            passed: true,
            score: 100,
            correct_count: 1,
            total_questions: 1,
            next_step: "sign_consent",
            message: "Passed comprehension check.",
          }),
        };
      }
      if (urlStr.includes("/api/v1/ai/biometrics/verify-liveness")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            liveness_token: "live-token-sub-101",
            is_live: true,
            anti_spoof_score: 0.99,
            expires_at: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
            status: "VERIFIED",
          }),
        };
      }
      if (urlStr.includes("/api/v1/auth/signature-verification")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ sig_token: "consent-sig-tok-1" }),
        };
      }
      if (
        urlStr.includes(
          "/api/v1/econsent/templates/tmpl-cardio-09/versions/3/capture-consent"
        )
      ) {
        return {
          ok: true,
          status: 201,
          json: async () => ({
            record_id: "rec-consent-01",
            status: "SIGNED",
          }),
        };
      }
      return { ok: true, status: 200, json: async () => [] };
    });

    await initializeApp();
    await settle();

    // IndexedDB scope uses OIDC sub, whereas step-up signer username resolves to preferred_username
    expect(getActiveUserId()).toBe(oidcSub);
    expect(resolveSessionSignerUsername()).toBe(preferredUsername);

    await initSessionKey("pin-secret-101", oidcSub); // pragma: allowlist secret
    await saveDraft("assign_sub_scope", { vssbp: "119" });
    expect(await getDraft("assign_sub_scope")).toEqual({ vssbp: "119" });

    // Load dynamic template/version and submit comprehension answers using OIDC sub as subject_pseudonym
    await loadConsentDetails();
    await settle();

    const radio = document.querySelector(
      'input[name="q_voluntary"][value="Yes"]'
    );
    expect(radio).not.toBeNull();
    radio.checked = true;
    await submitConsentAnswers();
    await settle();
    expect(state.consentPassed).toBe(true);

    const submitAnswersCall = calls.find((c) =>
      c.url.includes("/templates/tmpl-cardio-09/versions/3/submit-answers")
    );
    expect(submitAnswersCall?.body?.subject_pseudonym).toBe(oidcSub);

    // Step 2: Verify biometric liveness using OIDC sub as subject_pseudonym
    await verifyBiometricLiveness(true);
    await settle();
    expect(state.livenessVerified).toBe(true);
    const livenessCall = calls.find((c) =>
      c.url.includes("/api/v1/ai/biometrics/verify-liveness")
    );
    expect(livenessCall?.body?.subject_pseudonym).toBe(oidcSub);

    // Open SignatureModal for initial eConsent -> prefilled with preferred_username ("subject.site101")
    openSignatureModal("consent");
    await settle();

    const usernameInput = document.getElementById("sign-username");
    const passwordInput = document.getElementById("sign-password");
    expect(usernameInput.value).toBe(preferredUsername);

    passwordInput.value = "SubjectPass123!"; // pragma: allowlist secret
    await verifyAndSubmitSignature();
    await settle();

    const stepUpCall = calls.find((c) =>
      c.url.includes("/api/v1/auth/signature-verification")
    );
    expect(stepUpCall?.body).toMatchObject({
      username: preferredUsername,
      action:
        "/api/v1/econsent/templates/tmpl-cardio-09/versions/3/capture-consent",
      semantic_action: "econsent.consent.capture",
    });

    const captureCall = calls.find((c) =>
      c.url.includes("/templates/tmpl-cardio-09/versions/3/capture-consent")
    );
    expect(captureCall?.headers?.["X-Sig-Token"]).toBe("consent-sig-tok-1");
    expect(captureCall?.body).toMatchObject({
      subject_pseudonym: oidcSub,
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      signer_name: preferredUsername,
    });
    expect(state.consentSigned).toBe(true);
  });

  it("completes amendment re-consent with a single step-up X-Sig-Token bound to /api/v1/econsent/reconsent/complete/{requirement_id} and zero calls to /api/v1/execution/amendments/reconsent", async () => {
    const oidcSub = "7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d";
    const preferredUsername = "subject.site101";
    const jwt = encodeFakeJwt({
      sub: oidcSub,
      preferred_username: preferredUsername,
      study_ids: ["CADENCE-101"],
      site_id: "SITE-101",
    });

    state.session.userId = oidcSub;
    state.session.token = jwt;
    hydrateSessionScopeFromClaims(null, jwt);

    const calls = [];
    globalThis.fetch = vi.fn(async (url, init = {}) => {
      const urlStr = String(url);
      const parsedBody = init?.body ? JSON.parse(init.body) : null;
      calls.push({
        url: urlStr,
        method: init?.method || "GET",
        body: parsedBody,
        headers: init?.headers || {},
      });

      if (urlStr.includes("/api/v1/auth/signature-verification")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ sig_token: "single-use-reconsent-token" }),
        };
      }
      if (
        urlStr.includes("/api/v1/econsent/reconsent/complete/req_amend_v2_99")
      ) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            id: "req_amend_v2_99",
            status: "COMPLETED",
            completed: true,
            persisted: true,
          }),
        };
      }
      return { ok: true, status: 200, json: async () => [] };
    });

    await initializeApp();
    await settle();

    state.pendingReconsent = {
      id: "req_amend_v2_99",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      protocol_version: "2.0",
      new_version_index: 2,
      change_summary: "Updated remote blood pressure monitoring schedule.",
      comprehension_questions: [
        {
          id: "q_bp",
          text: "What was added in Version 2.0?",
          options: ["Remote blood pressure monitoring", "Inpatient admission"],
          correct_option: "Remote blood pressure monitoring",
        },
      ],
    };

    openReconsentModal();
    await settle();

    expect(state.reconsentForm.username).toBe(preferredUsername);
    state.reconsentComprehensionAnswers = {
      q_bp: "Remote blood pressure monitoring",
    };
    state.reconsentForm.password = "SubjectPass123!"; // pragma: allowlist secret

    await submitReconsentSignature();
    await settle();

    expect(state.pendingReconsent).toBeNull();
    expect(state.reconsentModalOpen).toBe(false);

    const stepUpCalls = calls.filter((c) =>
      c.url.includes("/api/v1/auth/signature-verification")
    );
    expect(stepUpCalls).toHaveLength(1);
    expect(stepUpCalls[0].body).toEqual({
      username: preferredUsername,
      password: "SubjectPass123!", // pragma: allowlist secret
      action: "/api/v1/econsent/reconsent/complete/req_amend_v2_99",
      semantic_action: "econsent.reconsent.sign",
    });

    const completeCalls = calls.filter((c) =>
      c.url.includes("/api/v1/econsent/reconsent/complete/req_amend_v2_99")
    );
    expect(completeCalls).toHaveLength(1);
    expect(completeCalls[0].headers["X-Sig-Token"]).toBe(
      "single-use-reconsent-token"
    );
    expect(completeCalls[0].body).toMatchObject({
      subject_pseudonym: oidcSub,
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      signer_name: preferredUsername,
      protocol_version: "2.0",
      version_index: 2,
      comprehension_answers: {
        q_bp: "Remote blood pressure monitoring",
      },
    });

    // Dead /api/v1/execution/amendments/reconsent endpoint is never called
    expect(
      calls.some((c) =>
        c.url.includes("/api/v1/execution/amendments/reconsent")
      )
    ).toBe(false);
  });

  it("renders ClinicalModal and ModalErrorAlert on SignatureModal and ReconsentModal failures while wiping passwords, keeping custom reasons, and refocusing password inputs", async () => {
    const oidcSub = "7a8b9c4d-5e6f-4a1b-8c2d-3e4f5a6b7c8d";
    const preferredUsername = "subject.site101";
    const jwt = encodeFakeJwt({
      sub: oidcSub,
      preferred_username: preferredUsername,
      study_ids: ["CADENCE-101"],
      site_id: "SITE-101",
    });

    state.session.userId = oidcSub;
    state.session.token = jwt;
    hydrateSessionScopeFromClaims(null, jwt);

    globalThis.fetch = vi.fn(async (url) => {
      const urlStr = String(url);
      if (urlStr.includes("/api/v1/auth/signature-verification")) {
        return {
          ok: false,
          status: 401,
          json: async () => ({ error: "Invalid credentials" }),
        };
      }
      return { ok: true, status: 200, json: async () => [] };
    });

    await initializeApp();
    await settle();

    // 1. SignatureModal failure recovery (401 from /api/v1/auth/signature-verification)
    state.consentComprehensionPassed = true;
    state.consentPassed = true;
    state.livenessVerified = true;
    state.consentLivenessToken = "live-token-err-01";
    state.consentLivenessExpiresAt = new Date(
      Date.now() + 10 * 60 * 1000
    ).toISOString();
    openSignatureModal("consent");
    await settle();

    const signDialog = document.getElementById("portal-sign-modal");
    expect(signDialog?.classList.contains("clinical-modal-dialog")).toBe(true);

    const signReasonSelect = document.getElementById("sign-reason");
    const signCustomReason = document.getElementById("sign-reason-custom");
    const signPassword = document.getElementById("sign-password");

    signReasonSelect.value = "Other";
    signReasonSelect.dispatchEvent(new Event("change", { bubbles: true }));
    signCustomReason.value = "Reviewed consent clauses with study nurse";
    signPassword.value = "WrongPassword99!"; // pragma: allowlist secret

    await verifyAndSubmitSignature();
    await settle();

    const signAlert = document.getElementById("modal-error-banner");
    expect(signAlert).not.toBeNull();
    expect(signAlert.classList.contains("modal-error-alert")).toBe(true);
    expect(signAlert.getAttribute("role")).toBe("alert");
    expect(signAlert.getAttribute("aria-live")).toBe("assertive");
    expect(/\p{Extended_Pictographic}/u.test(signAlert.textContent || "")).toBe(
      false
    );
    expect(signAlert.textContent).toContain(
      "Your selected signing reason has been kept."
    );
    expect(signPassword.value).toBe("");
    expect(signReasonSelect.value).toBe("Other");
    expect(signCustomReason.value).toBe(
      "Reviewed consent clauses with study nurse"
    );
    expect(document.activeElement).toBe(signPassword);

    // 2. ReconsentModal failure recovery
    state.signatureModalOpen = false;
    state.pendingReconsent = {
      id: "req_amend_err_01",
      study_id: "CADENCE-101",
      site_id: "SITE-101",
      protocol_version: "2.0",
      comprehension_questions: [
        {
          id: "q_check",
          text: "Do you agree to the updated schedule?",
          options: ["Yes", "No"],
          correct_option: "Yes",
        },
      ],
    };

    openReconsentModal();
    await settle();

    const reconsentDialog = document.getElementById("portal-reconsent-modal");
    expect(reconsentDialog?.classList.contains("clinical-modal-dialog")).toBe(
      true
    );

    state.reconsentComprehensionAnswers = { q_check: "Yes" };
    state.reconsentForm.reason = "Other";
    state.reconsentForm.customReason = "Discussed Week 8 check with PI";
    state.reconsentForm.password = "WrongPassword99!"; // pragma: allowlist secret
    await nextTick();

    await submitReconsentSignature();
    await settle();

    const reconsentAlert = document.getElementById(
      "reconsent-modal-error-banner"
    );
    const reconsentPassword = document.getElementById(
      "reconsent-sign-password"
    );
    expect(reconsentAlert).not.toBeNull();
    expect(reconsentAlert.classList.contains("modal-error-alert")).toBe(true);
    expect(reconsentAlert.getAttribute("role")).toBe("alert");
    expect(reconsentAlert.getAttribute("aria-live")).toBe("assertive");
    expect(
      /\p{Extended_Pictographic}/u.test(reconsentAlert.textContent || "")
    ).toBe(false);
    expect(reconsentAlert.textContent).toContain(
      "Your comprehension answers and signing reason have been kept."
    );
    expect(state.reconsentForm.password).toBe("");
    expect(reconsentPassword.value).toBe("");
    expect(state.reconsentForm.reason).toBe("Other");
    expect(state.reconsentForm.customReason).toBe(
      "Discussed Week 8 check with PI"
    );
    expect(state.reconsentComprehensionAnswers).toEqual({ q_check: "Yes" });
    expect(document.activeElement).toBe(reconsentPassword);
  });
});
