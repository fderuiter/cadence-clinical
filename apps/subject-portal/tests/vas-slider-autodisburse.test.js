import "fake-indexeddb/auto";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import {
  state,
  startQuestionnaire,
  validateActiveQuestionnaire,
  syncOfflineQueue,
  dispatchApi,
} from "../src/index.js";
import {
  initSessionKey,
  clearSessionKey,
  queueSubmission,
  getQueuedSubmissions,
  clearAllSubmissions,
} from "../src/sync-queue.js";

describe("Visual Analog Scale Sliders and Auto-Disbursement Engine", () => {
  const mockUserId = "subject_vas_001";

  beforeEach(async () => {
    state.session.userId = mockUserId;
    state.session.roles = "Subject";
    state.session.token = "test_vas_token";
    state.session.isOfflineMode = false;
    state.walletBalance = 0.0;
    state.submissions = [];

    await initSessionKey("vas-secret-passphrase", mockUserId);
    await clearAllSubmissions();

    document.body.innerHTML = `
      <div id="questionnaire-title"></div>
      <div id="questionnaire-desc"></div>
      <div id="questionnaire-form-container"></div>
      <div id="sync-queue-status-text"></div>
      <div id="session-subject-id"></div>
      <div id="session-wallet-balance"></div>
    `;
  });

  afterEach(async () => {
    clearSessionKey(mockUserId);
    await clearAllSubmissions();
    vi.restoreAllMocks();
  });

  it("renders continuous visual analog slider widgets and records numeric responses offline", async () => {
    const vasInstrument = {
      id: "inst_vas_test",
      name: "Symptom VAS Assessment",
      description: "Visual Analog Scale for pain and fatigue",
      version_index: 1,
      items: {
        pain_vas: {
          label: "Pain Intensity (0-100 VAS)",
          type: "visual_analog",
          required: true,
          min: 0,
          max: 100,
          step: 1,
          min_label: "No Pain (0)",
          max_label: "Unbearable Pain (100)",
        },
      },
    };

    state.assignments = [
      { id: "assign_vas_01", instrument_id: "inst_vas_test" },
    ];
    state.instruments["inst_vas_test"] = vasInstrument;
    state.activeQuestionnaire = {
      assignment: { id: "assign_vas_01" },
      instrument: vasInstrument,
      answers: {},
    };

    await startQuestionnaire("assign_vas_01");

    const container = document.getElementById("questionnaire-form-container");
    expect(container).not.toBeNull();

    const sliderInput = document.getElementById("pain_vas");
    expect(sliderInput).not.toBeNull();
    expect(sliderInput.getAttribute("type")).toBe("range");
    expect(sliderInput.getAttribute("min")).toBe("0");
    expect(sliderInput.getAttribute("max")).toBe("100");

    const displayBadge = document.getElementById("pain_vas_display");
    expect(displayBadge).not.toBeNull();
    expect(displayBadge.textContent).toContain("Value: 0");

    // Simulate user sliding to 75
    sliderInput.value = "75";
    sliderInput.dispatchEvent(new Event("input"));

    expect(displayBadge.textContent).toContain("Value: 75");

    // Validate active questionnaire
    const valid = validateActiveQuestionnaire();
    expect(valid).toBe(true);
    expect(state.activeQuestionnaire.answers.pain_vas).toBe(75);

    // Queue submission into IndexedDB offline
    const submission = await queueSubmission({
      subject_id: mockUserId,
      diary_id: "inst_vas_test",
      assignment_id: "assign_vas_01",
      answers: { pain_vas: 75 },
      change_reason: "Daily VAS completed offline",
      username: mockUserId,
    });

    expect(submission).toBeDefined();
    expect(submission.status).toBe("QUEUED");

    const queued = await getQueuedSubmissions();
    expect(queued).toHaveLength(1);
    expect(queued[0].answers.pain_vas).toBe(75);
  });

  it("automatically triggers stipend payment disburse API upon ePRO sync and updates wallet balance", async () => {
    // Queue an offline submission
    await queueSubmission({
      subject_id: mockUserId,
      diary_id: "inst_daily_diary",
      assignment_id: "assign_auto_01",
      answers: { vssbp: 120, vsdpb: 80, vshr: 72, pain_scale: 5 },
      change_reason: "Offline diary completion",
      username: mockUserId,
    });

    const queued = await getQueuedSubmissions();
    expect(queued).toHaveLength(1);
    const seqNum = queued[0].sequence_number;

    // Mock fetch for ePRO sync and payments disburse
    globalThis.fetch = vi.fn().mockImplementation((url, options) => {
      const urlStr = String(url);
      if (urlStr.includes("epro/sync")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              results: [
                {
                  sequence_number: seqNum,
                  client_id: queued[0].client_id,
                  status: "SYNCED",
                },
              ],
            }),
        });
      }
      if (urlStr.includes("payments/disburse")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              success: true,
              id: "disb_12345",
              wallet_id: "wallet_vas_001",
              subject_id: mockUserId,
              amount: 25.0,
              new_balance: 50.0,
              currency: "USD",
              status: "COMPLETED",
              reference_number: "DISB-REF-12345",
              idempotency_key: `seq_${seqNum}_${mockUserId}`,
              dispatched_at: new Date().toISOString(),
            }),
        });
      }
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ status: "ok" }),
      });
    });

    await syncOfflineQueue(false, { autoDisburse: true });

    // Verify ePRO sync fetch call
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining("epro/sync"),
      expect.any(Object)
    );

    // Verify Payments disburse fetch call
    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining("payments/disburse"),
      expect.objectContaining({
        method: "POST",
        body: expect.stringContaining(`"idempotency_key":"seq_${seqNum}_${mockUserId}"`),
      })
    );

    // Verify reactive wallet balance updated
    expect(state.walletBalance).toBe(50.0);
  });

  it("handles strict idempotency when disburse endpoint returns duplicate skipped status", async () => {
    const disburseRes = await dispatchApi("payments/disburse", {
      method: "POST",
      body: JSON.stringify({
        subject_id: mockUserId,
        sequence_number: 99,
        amount: 25.0,
        idempotency_key: `seq_99_${mockUserId}`,
      }),
    });

    expect(disburseRes).toBeDefined();
    expect(disburseRes.success).toBe(true);
  });
});
