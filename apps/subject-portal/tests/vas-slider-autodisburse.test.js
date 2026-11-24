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
    expect(displayBadge.textContent).toContain("Not answered");
    expect(displayBadge.getAttribute("data-state")).toBe("untouched");
    expect(sliderInput.getAttribute("data-touched")).toBe("false");
    expect(sliderInput.getAttribute("aria-valuetext")).toBe("Not answered");

    // Untouched required slider must fail validation instead of silently submitting 0
    expect(validateActiveQuestionnaire()).toBe(false);
    expect(
      document
        .getElementById("field-container-pain_vas")
        .classList.contains("has-error")
    ).toBe(true);

    // Simulate user sliding to 75
    sliderInput.value = "75";
    sliderInput.dispatchEvent(new Event("input", { bubbles: true }));

    expect(displayBadge.textContent).toContain("Value: 75");
    expect(displayBadge.getAttribute("data-state")).toBe("touched");
    expect(sliderInput.getAttribute("data-touched")).toBe("true");
    expect(sliderInput.hasAttribute("aria-valuetext")).toBe(false);

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

  it("accepts deliberate 0 VAS selection via pointerdown/keyboard while storing empty string for untouched optional VAS sliders", async () => {
    const mixedVasInstrument = {
      id: "inst_vas_mixed",
      name: "Mixed Required & Optional VAS",
      description: "Tests deliberate zero vs untouched optional VAS",
      version_index: 1,
      items: {
        pain_vas: {
          label: "Pain Intensity (0-100 VAS)",
          type: "visual_analog",
          required: true,
          min: 0,
          max: 100,
          step: 1,
        },
        fatigue_vas: {
          label: "Optional Fatigue (0-10 VAS)",
          type: "visual_analog",
          required: false,
          min: 0,
          max: 10,
          step: 1,
        },
      },
    };

    state.assignments = [
      { id: "assign_vas_mixed", instrument_id: "inst_vas_mixed" },
    ];
    state.instruments["inst_vas_mixed"] = mixedVasInstrument;

    await startQuestionnaire("assign_vas_mixed");

    const painSlider = document.getElementById("pain_vas");
    const fatigueSlider = document.getElementById("fatigue_vas");

    // Pressing Tab over the slider must NOT mark it touched
    painSlider.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Tab", bubbles: true })
    );
    expect(painSlider.getAttribute("data-touched")).toBe("false");
    expect(validateActiveQuestionnaire()).toBe(false);

    // Deliberately selecting 0 via Home/ArrowLeft keydown marks it touched with Value: 0
    painSlider.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Home", bubbles: true })
    );
    expect(painSlider.getAttribute("data-touched")).toBe("true");
    expect(document.getElementById("pain_vas_display").textContent).toContain(
      "Value: 0"
    );
    expect(fatigueSlider.getAttribute("data-touched")).toBe("false");

    expect(validateActiveQuestionnaire()).toBe(true);
    expect(state.activeQuestionnaire.answers.pain_vas).toBe(0);
    expect(state.activeQuestionnaire.answers.fatigue_vas).toBe("");
  });

  it("triggers server-rule diary stipend endpoint without client-supplied amount upon ePRO sync and updates wallet balance", async () => {
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

    let capturedStipendBody = null;
    vi.spyOn(globalThis, "fetch").mockImplementation((url, options) => {
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
      if (urlStr.includes("payments/participant/me/diary-stipend")) {
        capturedStipendBody = JSON.parse(options.body);
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              status: "CREDITED",
              creditedAmount: 25.0,
              currentBalance: 50.0,
              lifetimeStipends: 50.0,
              walletId: "wallet_vas_001",
              transactionId: "tx_12345",
              idempotencyKey: `seq_${seqNum}_${mockUserId}`,
              subjectId: mockUserId,
            }),
        });
      }
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ status: "ok" }),
      });
    });

    await syncOfflineQueue(false, { autoDisburse: true });

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining("epro/sync"),
      expect.any(Object)
    );

    expect(globalThis.fetch).toHaveBeenCalledWith(
      expect.stringContaining("payments/participant/me/diary-stipend"),
      expect.objectContaining({
        method: "POST",
      })
    );
    expect(capturedStipendBody).toBeDefined();
    expect(capturedStipendBody.idempotencyKey).toBe(
      `seq_${seqNum}_${mockUserId}`
    );
    expect(capturedStipendBody.diaryId).toBe("inst_daily_diary");
    expect(capturedStipendBody).not.toHaveProperty("amount");

    expect(state.walletBalance).toBe(50.0);
  });

  it("enforces strict idempotency and rejects client-supplied amount on diary stipend endpoint", async () => {
    const firstRes = await dispatchApi(
      "payments/participant/me/diary-stipend",
      {
        method: "POST",
        body: JSON.stringify({
          sequenceNumber: 99,
          diaryId: "inst_daily_diary",
          idempotencyKey: `seq_99_${mockUserId}`,
        }),
      }
    );
    expect(firstRes).toBeDefined();
    expect(firstRes.status).toBe("CREDITED");

    const duplicateRes = await dispatchApi(
      "payments/participant/me/diary-stipend",
      {
        method: "POST",
        body: JSON.stringify({
          sequenceNumber: 99,
          diaryId: "inst_daily_diary",
          idempotencyKey: `seq_99_${mockUserId}`,
        }),
      }
    );
    expect(duplicateRes.status).toBe("ALREADY_PROCESSED");

    await expect(
      dispatchApi("payments/participant/me/diary-stipend", {
        method: "POST",
        body: JSON.stringify({
          sequenceNumber: 100,
          diaryId: "inst_daily_diary",
          amount: 999.0,
          idempotencyKey: `seq_100_${mockUserId}`,
        }),
      })
    ).rejects.toThrow(/400|amount/i);
  });

  it("forwards syncVerificationToken from confirmed epro/sync and isolated epro/submit responses to diary-stipend", async () => {
    await queueSubmission({
      subject_id: mockUserId,
      diary_id: "inst_daily_diary",
      assignment_id: "assign_sync_tok_01",
      answers: { vssbp: 118, vsdpb: 76, vshr: 68, pain_scale: 3 },
      change_reason: "Offline diary with sync token",
      username: mockUserId,
    });

    const firstBatch = await getQueuedSubmissions();
    expect(firstBatch).toHaveLength(1);

    const capturedStipendPayloads = [];
    vi.spyOn(globalThis, "fetch").mockImplementation((url, options) => {
      const urlStr = String(url);
      if (urlStr.includes("epro/sync")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              results: [
                {
                  sequence_number: firstBatch[0].sequence_number,
                  client_id: firstBatch[0].client_id,
                  status: "SYNCED",
                  syncVerificationToken: "hmac-sync-token-batch-01",
                },
              ],
            }),
        });
      }
      if (urlStr.includes("payments/participant/me/diary-stipend")) {
        capturedStipendPayloads.push(JSON.parse(options.body));
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              status: "CREDITED",
              creditedAmount: 25.0,
              currentBalance: 25.0,
              lifetimeStipends: 25.0,
              walletId: "wallet_vas_001",
              transactionId: "tx_sync_tok_01",
              idempotencyKey: `seq_${firstBatch[0].sequence_number}_${mockUserId}`,
              subjectId: mockUserId,
            }),
        });
      }
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ status: "ok" }),
      });
    });

    await syncOfflineQueue(false, { autoDisburse: true });

    expect(capturedStipendPayloads).toHaveLength(1);
    expect(capturedStipendPayloads[0]).toMatchObject({
      diaryId: "inst_daily_diary",
      assignmentId: "assign_sync_tok_01",
      sequenceNumber: firstBatch[0].sequence_number,
      syncVerificationToken: "hmac-sync-token-batch-01",
    });

    // Now exercise isolated epro/submit fallback with snake_case sync_verification_token
    vi.restoreAllMocks();
    await clearAllSubmissions();
    await queueSubmission({
      subject_id: mockUserId,
      diary_id: "inst_daily_diary",
      assignment_id: "assign_submit_tok_02",
      answers: { vssbp: 122, vsdpb: 79, vshr: 70, pain_scale: 4 },
      change_reason: "Isolated fallback diary with sync token",
      username: mockUserId,
    });

    const secondBatch = await getQueuedSubmissions();
    expect(secondBatch).toHaveLength(1);

    const isolatedStipendPayloads = [];
    vi.spyOn(globalThis, "fetch").mockImplementation((url, options) => {
      const urlStr = String(url);
      if (urlStr.includes("epro/sync")) {
        return Promise.resolve({
          ok: false,
          status: 422,
          json: () =>
            Promise.resolve({
              detail: "Batch schema validation fallback trigger",
            }),
        });
      }
      if (urlStr.includes("epro/submit")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              status: "CREATED",
              sync_verification_token: "hmac-sync-token-isolated-02",
            }),
        });
      }
      if (urlStr.includes("payments/participant/me/diary-stipend")) {
        isolatedStipendPayloads.push(JSON.parse(options.body));
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              status: "CREDITED",
              creditedAmount: 25.0,
              currentBalance: 50.0,
              lifetimeStipends: 50.0,
              walletId: "wallet_vas_001",
              transactionId: "tx_sync_tok_02",
              idempotencyKey: `seq_${secondBatch[0].sequence_number}_${mockUserId}`,
              subjectId: mockUserId,
            }),
        });
      }
      return Promise.resolve({
        ok: true,
        json: () => Promise.resolve({ status: "ok" }),
      });
    });

    await syncOfflineQueue(false, { autoDisburse: true });

    expect(isolatedStipendPayloads).toHaveLength(1);
    expect(isolatedStipendPayloads[0]).toMatchObject({
      diaryId: "inst_daily_diary",
      assignmentId: "assign_submit_tok_02",
      sequenceNumber: secondBatch[0].sequence_number,
      syncVerificationToken: "hmac-sync-token-isolated-02",
    });
  });
});
