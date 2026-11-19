// Verified Portal eConsent Gating and Credential Hygiene Tests
import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, vi } from "vitest";

beforeEach(async () => {
  window.__MOCK_TEST_ENV__ = true;
  document.head.innerHTML = `<link rel="manifest" href="/subject-portal/manifest.json" />`;
  document.body.innerHTML = `
    <div id="app">
      <nav class="portal-navigation">
        <ul class="nav-tabs">
          <li id="tab-btn-tasks" class="nav-item"><button type="button">My Tasks</button></li>
          <li id="tab-btn-compliance" class="nav-item"><button type="button">My Compliance</button></li>
          <li id="tab-btn-inbox" class="nav-item"><button type="button">My Inbox <span id="unread-count">0</span></button></li>
          <li id="tab-btn-consent" class="nav-item"><button type="button">My Consent</button></li>
        </ul>
        <span id="session-subject-id">subject_001</span>
      </nav>

      <main class="portal-main">
        <!-- Tasks View -->
        <section id="view-tasks" class="portal-view">
          <div id="tasks-loading" style="display: none;"></div>
          <div id="tasks-failure" style="display: none;">
            <span id="tasks-error-msg"></span>
            <button type="button" id="btn-retry-tasks">Retry</button>
          </div>
          <div id="tasks-list-container"></div>
          <div id="sync-queue-list"></div>
          <div id="sync-queue-status-text"></div>
          <button type="button" id="btn-sync-now">Sync</button>
        </section>

        <!-- Compliance View -->
        <section id="view-compliance" class="portal-view">
          <div id="compliance-loading" style="display: none;"></div>
          <div id="compliance-failure" style="display: none;">
            <span id="compliance-error-msg"></span>
            <button type="button" id="btn-retry-compliance">Retry</button>
          </div>
          <div class="grid-layout">
            <div id="compliance-rate-pct">0%</div>
            <div id="compliance-completed-count">0</div>
            <div id="compliance-pending-count">0</div>
            <div id="compliance-overdue-count">0</div>
            <table>
              <tbody id="compliance-history-tbody"></tbody>
            </table>
          </div>
        </section>

        <!-- Inbox View -->
        <section id="view-inbox" class="portal-view">
          <div id="inbox-loading" style="display: none;"></div>
          <div id="inbox-failure" style="display: none;">
            <span id="inbox-error-msg"></span>
            <button type="button" id="btn-retry-inbox">Retry</button>
          </div>
          <div id="inbox-container"></div>
        </section>

        <!-- Consent View -->
        <section id="view-consent" class="portal-view">
          <select id="consent-lang-selector">
            <option value="en">English (en)</option>
            <option value="es">Español (es)</option>
          </select>
          <span id="consent-status-badge" class="status-pill pending">Pending Check</span>

          <div id="consent-loading" style="display: none;">Loading...</div>
          <div id="consent-failure" style="display: none;">
            <span id="consent-error-msg"></span>
            <button type="button" id="btn-retry-consent">Retry</button>
          </div>

          <div id="consent-content-wrapper" style="display: none;">
            <h3 id="consent-template-title"></h3>
            <div id="consent-metadata-display"></div>
            <div id="consent-clauses-container"></div>

            <div id="consent-comprehension-card">
              <div id="comprehension-status-banner" style="display: none;" role="status" aria-live="polite"></div>
              <div id="consent-questions-container"></div>
              <button type="button" id="btn-submit-consent-answers">Submit Answers</button>
            </div>

            <div id="consent-liveness-card">
              <div id="liveness-status-banner" style="display: none;" role="status" aria-live="polite"></div>
              <button type="button" id="btn-verify-liveness">Verify Liveness</button>
              <button type="button" id="btn-liveness-fallback">Fallback</button>
            </div>

            <div id="consent-signature-card">
              <button type="button" id="btn-trigger-consent-sign" disabled>Sign Consent</button>
            </div>
          </div>
        </section>
      </main>

      <!-- Signature Modal -->
      <div id="portal-sign-modal" style="display: none;" role="dialog" aria-modal="true" aria-labelledby="portal-modal-title">
        <div class="modal">
          <div class="modal-header" id="portal-modal-title">Electronic Signature Required</div>
          <div class="modal-body">
            <div id="modal-error-banner" style="display: none;" role="status" aria-live="polite"></div>
            <select id="sign-reason"></select>
            <textarea id="sign-reason-custom"></textarea>
            <input type="text" id="sign-username" />
            <input type="password" id="sign-password" />
          </div>
          <button type="button" id="btn-modal-cancel">Cancel</button>
          <button type="button" id="btn-modal-sign">Sign</button>
        </div>
      </div>

      <div id="portal-ledger-timeline"></div>
    </div>
  `;

  globalThis.fetch = vi.fn().mockImplementation(() =>
    Promise.resolve({
      ok: true,
      json: () => Promise.resolve({ status: "success" }),
    })
  );

  const portal = await import("../src/index.js");
  portal.state.session.userId = "subject_001";
  portal.state.session.token = null;
  portal.state.session.isOfflineMode = true;
  portal.state.session.isDemoMode = true;
  portal.state.consentLanguage = "en";
  portal.state.consentPassed = false;
  portal.state.consentSigned = false;
  portal.state.pendingSignableAction = null;
  portal.state.consentContent = null;
  portal.state.consentCheck = null;
  portal.state.consentLivenessToken = null;
  portal.state.livenessVerified = false;
  portal.state.ledgerBlocks = [];
});

describe("eConsent Patient Portal - Workflow and Gating Integration Tests", () => {
  it("verifies language selection drop-down re-fetches approved consent content", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();

    portal.showView("view-consent");
    expect(portal.state.consentLanguage).toBe("en");

    // Change language to Spanish
    const langSelect = document.getElementById("consent-lang-selector");
    langSelect.value = "es";
    langSelect.dispatchEvent(new Event("change"));

    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(portal.state.consentLanguage).toBe("es");
    expect(document.getElementById("consent-template-title").textContent).toBe(
      "Formulario de Consentimiento Informado Principal"
    );
  });

  it("gates the Sign action and keeps signature disabled until comprehension verification passes", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();

    portal.showView("view-consent");

    const btnTriggerSign = document.getElementById("btn-trigger-consent-sign");
    expect(btnTriggerSign.disabled).toBe(true);

    // Try submitting incorrect answers (forces failure)
    const qContainer = document.getElementById("consent-questions-container");
    expect(qContainer.innerHTML).toContain("q_headache");

    // Check wrong choices
    document.getElementById("q_headache_option_1").checked = true; // Nausea (wrong)
    document.getElementById("q_withdraw_option_1").checked = true; // No (wrong)

    await portal.submitConsentAnswers();

    // Check still blocked and failed status is shown
    expect(btnTriggerSign.disabled).toBe(true);
    expect(portal.state.consentPassed).toBe(false);

    const banner = document.getElementById("comprehension-status-banner");
    expect(banner.style.display).toBe("block");
    expect(banner.textContent).toContain(
      "Please review the material and try again"
    );

    // Now check correct choices
    document.getElementById("q_headache_option_0").checked = true; // Headache (correct)
    document.getElementById("q_withdraw_option_0").checked = true; // Yes (correct)

    await portal.submitConsentAnswers();

    // Comprehension check passed, but Sign button remains disabled until liveness check is complete
    expect(portal.state.consentPassed).toBe(true);
    expect(btnTriggerSign.disabled).toBe(true);
    expect(banner.textContent).toContain("Congratulations");

    // Perform liveness verification
    await portal.verifyBiometricLiveness();

    // Sign action should now be fully available
    expect(portal.state.livenessVerified).toBe(true);
    expect(portal.state.consentLivenessToken).toBeDefined();
    expect(btnTriggerSign.disabled).toBe(false);
  });

  it("performs biometric liveness verification and sets liveness token in state", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();
    await portal.loadConsentDetails();

    await portal.verifyBiometricLiveness(false);

    expect(portal.state.livenessVerified).toBe(true);
    expect(portal.state.consentLivenessToken).toContain("mock_liveness_token");
    expect(portal.state.consentLivenessToken).toContain("camera");

    const livenessBanner = document.getElementById("liveness-status-banner");
    expect(livenessBanner.style.display).toBe("block");
    expect(livenessBanner.textContent).toContain(
      "Liveness Verification Successful"
    );
  });

  it("supports study coordinator video fallback for liveness verification", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();
    await portal.loadConsentDetails();

    await portal.verifyBiometricLiveness(true);

    expect(portal.state.livenessVerified).toBe(true);
    expect(portal.state.consentLivenessToken).toContain("fallback");

    const livenessBanner = document.getElementById("liveness-status-banner");
    expect(livenessBanner.style.display).toBe("block");
    expect(livenessBanner.textContent).toContain("Coordinator Fallback Mode");
  });

  it("verifies strict credential hygiene: cleans PIN/password inputs immediately on submit or cancel", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();

    portal.state.consentPassed = true;
    portal.state.livenessVerified = true;
    portal.state.consentLivenessToken = "mock-token-123";
    await portal.loadConsentDetails(); // Populates tpl and check structures correctly
    portal.renderConsentUI();

    const btnTriggerSign = document.getElementById("btn-trigger-consent-sign");
    expect(btnTriggerSign.disabled).toBe(false);
    btnTriggerSign.click();

    const modal = document.getElementById("portal-sign-modal");
    expect(modal.style.display).toBe("flex");

    const usernameInput = document.getElementById("sign-username");
    const passwordInput = document.getElementById("sign-password");

    usernameInput.value = "subject_001";
    passwordInput.value = "my-secret-pin-4444";

    // Trigger verify signature submit
    await portal.verifyAndSubmitSignature();

    // Password must be instantly cleaned
    expect(passwordInput.value).toBe("");
    expect(portal.state.consentSigned).toBe(true);
  });

  it("verifies credential hygiene on cancel or failure", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();

    portal.state.consentPassed = true;
    portal.state.livenessVerified = true;
    portal.state.consentLivenessToken = "mock-token-123";
    await portal.loadConsentDetails();
    portal.renderConsentUI();

    const btnTriggerSign = document.getElementById("btn-trigger-consent-sign");
    btnTriggerSign.click();

    const passwordInput = document.getElementById("sign-password");
    passwordInput.value = "dont-leak-me";

    // Cancel modal
    document.getElementById("btn-modal-cancel").click();

    expect(passwordInput.value).toBe("");
  });

  it("asserts accessibility attributes on the new consent review section and signing modal", async () => {
    const modal = document.getElementById("portal-sign-modal");
    expect(modal.getAttribute("role")).toBe("dialog");
    expect(modal.getAttribute("aria-modal")).toBe("true");
    expect(modal.getAttribute("aria-labelledby")).toBe("portal-modal-title");

    const errorBanner = document.getElementById("modal-error-banner");
    expect(errorBanner.getAttribute("role")).toBe("status");
    expect(errorBanner.getAttribute("aria-live")).toBe("polite");

    const compBanner = document.getElementById("comprehension-status-banner");
    expect(compBanner.getAttribute("role")).toBe("status");
    expect(compBanner.getAttribute("aria-live")).toBe("polite");
  });

  it("posts ADR-2259 step-up verification and CaptureConsentRequest fields when authenticated, and focuses #sign-password on error", async () => {
    const portal = await import("../src/index.js");
    await portal.initializeApp();

    portal.state.session.userId = "subject_001";
    portal.state.session.token = "live-session-jwt"; // pragma: allowlist secret
    portal.state.session.isOfflineMode = false;
    portal.state.session.isDemoMode = false;
    portal.state.consentPassed = true;
    portal.state.livenessVerified = true;
    portal.state.consentLivenessToken = "live-liveness-token-001";
    portal.state.consentContent = {
      template_id: "template-icf",
      template_name: "Main Informed Consent Form",
      study_id: "study_01",
      site_id: "site_alpha",
      protocol_version: "v1.0",
      language_code: "en",
      version_index: 1,
      clauses: [],
    };
    portal.renderConsentUI();

    const calls = [];
    let failStepUp = true;
    globalThis.fetch = vi.fn().mockImplementation(async (url, opts) => {
      calls.push({ url: String(url), opts });
      if (String(url).includes("/api/v1/auth/signature-verification")) {
        if (failStepUp) {
          return {
            ok: false,
            status: 401,
            json: async () => ({ detail: "INVALID_CREDENTIALS" }),
          };
        }
        return {
          ok: true,
          json: async () => ({ sig_token: "sig-token-live-1", expires_in: 60 }),
        };
      }
      if (
        String(url).includes(
          "/api/v1/econsent/templates/template-icf/versions/1/capture-consent"
        )
      ) {
        return {
          ok: true,
          json: async () => ({
            id: "consent-1",
            status: "ACTIVE",
          }),
        };
      }
      return { ok: true, json: async () => ({}) };
    });

    document.getElementById("btn-trigger-consent-sign").click();
    const usernameInput = document.getElementById("sign-username");
    const passwordInput = document.getElementById("sign-password");
    usernameInput.value = "subject_001";
    passwordInput.value = "wrong-pin"; // pragma: allowlist secret

    // Failed step-up wipes password, keeps modal open, displays error, and focuses #sign-password
    await portal.verifyAndSubmitSignature();
    expect(passwordInput.value).toBe("");
    expect(portal.state.consentSigned).toBe(false);
    expect(document.activeElement?.id).toBe("sign-password");
    expect(document.getElementById("modal-error-banner").textContent).toContain(
      "Signature Verification Rejected"
    );

    // Retry with valid credentials sends full CaptureConsentRequest to /capture-consent
    failStepUp = false;
    calls.length = 0;
    passwordInput.value = "correct-pin"; // pragma: allowlist secret
    await portal.verifyAndSubmitSignature();

    const stepUpCall = calls.find((c) =>
      c.url.includes("/api/v1/auth/signature-verification")
    );
    expect(stepUpCall).toBeDefined();
    expect(JSON.parse(stepUpCall.opts.body)).toEqual({
      username: "subject_001",
      password: "correct-pin", // pragma: allowlist secret
      action:
        "/api/v1/econsent/templates/template-icf/versions/1/capture-consent",
      semantic_action: "econsent.consent.capture",
    });

    const captureCall = calls.find((c) =>
      c.url.includes(
        "/api/v1/econsent/templates/template-icf/versions/1/capture-consent"
      )
    );
    expect(captureCall).toBeDefined();
    expect(captureCall.opts.headers["X-Sig-Token"]).toBe("sig-token-live-1");
    const captureBody = JSON.parse(captureCall.opts.body);
    expect(captureBody).toMatchObject({
      subject_pseudonym: "subject_001",
      study_id: "study_01",
      site_id: "site_alpha",
      signer_name: "subject_001",
      signer_role: "Subject",
      meaning: "Enrollment Confirmation and Consent",
      signing_reason: "Enrollment Confirmation and Consent",
      protocol_version: "v1.0",
      signature_data: "subject_001",
      liveness_token: "live-liveness-token-001",
    });
    expect(captureBody.digest_sha256).toMatch(/^[0-9a-f]{64}$/);
    expect(portal.state.consentSigned).toBe(true);
  });
});
