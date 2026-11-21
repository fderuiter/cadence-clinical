import "fake-indexeddb/auto";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  MOCK_APPROVED_CONTENT,
  MOCK_ASSIGNMENTS,
  MOCK_NOTIFICATIONS,
} from "../src/data/mock-data.js";
import { initializeApp, state } from "../src/index.js";
import {
  clearInMemoryKey,
  getClientId,
  initSessionKey,
  setActiveUserId,
} from "../src/sync-queue.js";
import {
  demoNowIso,
  demoRandom,
  resetDemoRandom,
} from "../src/utils/demoMode.js";

describe("Subject Portal Canonical Demo Manifest & Deterministic Genesis/Crypto", () => {
  beforeEach(() => {
    window.__MOCK_TEST_ENV__ = true;
    localStorage.clear();
    sessionStorage.clear();
    clearInMemoryKey();
    resetDemoRandom();
    document.body.innerHTML = `<div id="app"></div>`;
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
    clearInMemoryKey();
    resetDemoRandom();
  });

  it("seeds CADENCE-101, SUBJ-101-001, and fixed epoch task dates in MOCK_ASSIGNMENTS and MOCK_NOTIFICATIONS", () => {
    expect(MOCK_APPROVED_CONTENT.study_id).toBe("CADENCE-101");
    expect(MOCK_APPROVED_CONTENT.site_id).toBe("SITE-101");
    expect(MOCK_APPROVED_CONTENT.sponsor_code).toBe("CADENCE-PHARMA");

    expect(MOCK_ASSIGNMENTS.map((a) => a.subject_id)).toEqual([
      "SUBJ-101-001",
      "SUBJ-101-001",
      "SUBJ-101-001",
    ]);
    expect(MOCK_ASSIGNMENTS[0].due_at).toBe("2026-01-05T13:00:00.000Z");
    expect(MOCK_ASSIGNMENTS[0].end_date).toBe("2026-01-06T09:00:00.000Z");
    expect(MOCK_ASSIGNMENTS[1].due_at).toBe("2026-01-05T07:00:00.000Z");
    expect(MOCK_ASSIGNMENTS[1].end_date).toBe("2026-01-05T21:00:00.000Z");
    expect(MOCK_ASSIGNMENTS[2].due_at).toBe("2026-01-03T09:00:00.000Z");
    expect(MOCK_ASSIGNMENTS[2].end_date).toBe("2026-01-04T09:00:00.000Z");
    expect(MOCK_ASSIGNMENTS[2].submitted_at).toBe("2026-01-03T10:00:00.000Z");

    expect(MOCK_NOTIFICATIONS[0].due_at).toBe("2026-01-05T13:00:00.000Z");
    expect(MOCK_NOTIFICATIONS[1].due_at).toBe("2026-01-05T07:00:00.000Z");
  });

  it("produces identical SHA-256 Genesis hashes and task dates across repeated demo boots", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");

    const runDemoBoot = async () => {
      localStorage.clear();
      sessionStorage.clear();
      clearInMemoryKey();
      resetDemoRandom();
      state.ledgerBlocks = [];
      state.assignments = [];
      state.notifications = [];
      state.session.userId = "SUBJ-101-001";
      state.session.studyId = "CADENCE-101";
      state.session.siteId = "SITE-101";
      state.session.token = null;
      state.session.isDemoMode = true;
      state.session.isOfflineMode = true;
      document.body.innerHTML = `<div id="app"></div>`;

      await initializeApp();

      return {
        genesis: JSON.parse(JSON.stringify(state.ledgerBlocks[0])),
        assignmentDates: state.assignments.map((a) => ({
          id: a.id,
          due_at: a.due_at,
          end_date: a.end_date,
        })),
        clientId: await getClientId(),
      };
    };

    const bootA = await runDemoBoot();
    const bootB = await runDemoBoot();

    expect(bootA.genesis.action).toBe("GENESIS");
    expect(bootA.genesis.timestamp).toBe("2026-01-05T09:00:00.000Z");
    expect(bootA.genesis.hash).toMatch(/^[0-9a-f]{64}$/);
    expect(bootB.genesis).toEqual(bootA.genesis);
    expect(bootB.assignmentDates).toEqual(bootA.assignmentDates);
    expect(bootB.clientId).toBe(bootA.clientId);
  });

  it("uses deterministic seeded salt in demo mode and Web Crypto getRandomValues outside demo mode", async () => {
    const getRandomValuesSpy = vi.spyOn(globalThis.crypto, "getRandomValues");

    // 1. Demo mode: deterministic salt & client ID without calling getRandomValues
    vi.stubEnv("VITE_DEMO_MODE", "true");
    state.session.isDemoMode = true;
    setActiveUserId("demo_salt_check_1");
    resetDemoRandom();
    await initSessionKey("demo-passphrase", "demo_salt_check_1");
    expect(getRandomValuesSpy).not.toHaveBeenCalled();

    // 2. Non-demo mode: live system clock + Web Crypto getRandomValues
    vi.stubEnv("VITE_DEMO_MODE", "false");
    state.session.isDemoMode = false;
    setActiveUserId("prod_salt_check_1");
    clearInMemoryKey();
    await initSessionKey("live-passphrase", "prod_salt_check_1");

    expect(getRandomValuesSpy).toHaveBeenCalled();
    expect(demoNowIso(0)).not.toBe("2026-01-05T09:00:00.000Z");

    const randVal = demoRandom("live-check");
    expect(randVal).toBeGreaterThanOrEqual(0);
    expect(randVal).toBeLessThan(1);
  });
});
