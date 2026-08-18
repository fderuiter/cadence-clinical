import { describe, it, expect, beforeEach } from "vitest";
import {
  state,
  openReconsentModal,
  closeReconsentModal,
  submitReconsentSignature,
  renderInbox,
} from "../src/index.js";

describe("Subject Portal Immediate Re-Consent Workflow", () => {
  beforeEach(() => {
    if (typeof window !== "undefined") {
      window.__MOCK_TEST_ENV__ = true;
    }
    // Reset state before each test
    state.pendingReconsent = null;
    state.reconsentModalOpen = false;
    state.reconsentModalError = "";
    state.reconsentForm.username = "";
    state.reconsentForm.password = "";
    state.reconsentForm.reason = "Protocol Amendment Re-Consent Acknowledgment";
    state.reconsentForm.customReason = "";
    state.consentSigned = false;
    state.ledgerBlocks = [];
    state.notifications = [];
  });

  it("opens interactive reconsent modal with default requirement payload", () => {
    openReconsentModal();
    expect(state.reconsentModalOpen).toBe(true);
    expect(state.pendingReconsent).not.toBeNull();
    expect(state.pendingReconsent.protocol_version).toBe("2.0");
  });

  it("renders reconsent alert in notification inbox with Review & Sign button", () => {
    document.body.innerHTML = `
      <div id="inbox-loading" style="display:none"></div>
      <div id="inbox-failure" style="display:none"></div>
      <div id="inbox-error-msg"></div>
      <div id="unread-count"></div>
      <div id="inbox-container"></div>
    `;

    state.notifications = [
      {
        id: "notif_reconsent_01",
        channel: "IN_APP",
        message_content: "URGENT: Protocol amendment re-consent required for study STUDY-01.",
        is_read: false,
        related_entity_type: "RECONSENT_REQUIRED",
      },
    ];

    renderInbox();

    const container = document.getElementById("inbox-container");
    expect(container.innerHTML).toContain("URGENT: Protocol amendment re-consent required");
    expect(container.innerHTML).toContain("Review &amp; Sign");
  });

  it("validates empty username and password fields on submitReconsentSignature", async () => {
    openReconsentModal();
    state.reconsentForm.username = "";
    await submitReconsentSignature();
    expect(state.reconsentModalError).toContain("Please enter your User ID / Username.");

    state.reconsentForm.username = "subject_001";
    await submitReconsentSignature();
    expect(state.reconsentModalError).toContain("Please enter your Security PIN / Password.");
  });

  it("executes e-signature, logs 21 CFR Part 11 audit record, and unlocks submission status", async () => {
    openReconsentModal();
    state.reconsentForm.username = "subject_001";
    state.reconsentForm.password = "secret_pin_123";

    await submitReconsentSignature();

    expect(state.pendingReconsent).toBeNull();
    expect(state.consentSigned).toBe(true);
    expect(state.reconsentModalOpen).toBe(false);

    // Verify Part 11 ledger audit block entry
    expect(state.ledgerBlocks.length).toBeGreaterThan(0);
    const auditRecord = state.ledgerBlocks[state.ledgerBlocks.length - 1];
    expect(auditRecord.action).toBe("RECONSENT_EXECUTED");
    expect(auditRecord.details.subject_id).toBe("subject_001");
  });
});
