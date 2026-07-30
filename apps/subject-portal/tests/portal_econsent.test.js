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

  const portal = await import("../index.js");
  portal.state.session.userId = "subject_001";
  portal.state.session.token = null;
  portal.state.session.isOfflineMode = true;
  portal.state.consentLanguage = "en";
  portal.state.consentPassed = false;
  portal.state.consentSigned = false;
  portal.state.pendingSignableAction = null;
  portal.state.consentContent = null;
  portal.state.consentCheck = null;
  portal.state.ledgerBlocks = [];
});

describe("eConsent Patient Portal - Workflow and Gating Integration Tests", () => {
  it("verifies language selection drop-down re-fetches approved consent content", async () => {
    const portal = await import("../index.js");
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
    const portal = await import("../index.js");
    await portal.initializeApp();

    portal.showView("view-consent");

    const btnTriggerSign = document.getElementById("btn-trigger-consent-sign");
    expect(btnTriggerSign.disabled).toBe(true);

    // Try submitting incorrect answers (forces failure)
    const qContainer = document.getElementById("consent-questions-container");
    expect(qContainer.innerHTML).toContain("q_headache");

    // Check wrong choices
    document.getElementById("q_headache_option_1").checked = true; // Nausea (wrong)
    document.getElementById("q_withdraw_option_1").checked = true;  // No (wrong)

    await portal.submitConsentAnswers();

    // Check still blocked and failed status is shown
    expect(btnTriggerSign.disabled).toBe(true);
    expect(portal.state.consentPassed).toBe(false);

    const banner = document.getElementById("comprehension-status-banner");
    expect(banner.style.display).toBe("block");
    expect(banner.textContent).toContain("Please review the material and try again");

    // Now check correct choices
    document.getElementById("q_headache_option_0").checked = true; // Headache (correct)
    document.getElementById("q_withdraw_option_0").checked = true;  // Yes (correct)

    await portal.submitConsentAnswers();

    // Sign action should now be fully available
    expect(btnTriggerSign.disabled).toBe(false);
    expect(portal.state.consentPassed).toBe(true);
    expect(banner.textContent).toContain("Congratulations");
  });

  it("verifies strict credential hygiene: cleans PIN/password inputs immediately on submit or cancel", async () => {
    const portal = await import("../index.js");
    await portal.initializeApp();

    portal.state.consentPassed = true;
    await portal.loadConsentDetails(); // Populates tpl and check structures correctly
    portal.renderConsentUI();

    const btnTriggerSign = document.getElementById("btn-trigger-consent-sign");
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
    const portal = await import("../index.js");
    await portal.initializeApp();

    portal.state.consentPassed = true;
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
});
