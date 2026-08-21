import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../../src/stores/clinical";
import { useAuthStore } from "../../src/stores/auth";
import { executionService } from "../../src/api/execution";

// Mock execution service
vi.mock("../../src/api/execution", () => ({
  executionService: {
    createSubject: vi.fn(),
    screenSubject: vi.fn(),
    listLabAlerts: vi.fn().mockResolvedValue([]),
  },
}));

describe("Clinical Store & Auth Store - Subject Screening & CRC Gating Specification", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    if (typeof window !== "undefined" && window.localStorage) {
      window.localStorage.clear();
    }
  });

  it("authStore.hasRole and authStore.isCrc resolve CRC and investigator roles correctly", () => {
    const authStore = useAuthStore();

    // Set CRC role
    authStore.rawRoles = ["Site Investigator"];
    expect(authStore.isCrc).toBe(true);
    expect(authStore.hasRole("crc")).toBe(true);

    // Set Monitor role
    authStore.rawRoles = ["CRA", "Monitor"];
    expect(authStore.isCrc).toBe(false);
    expect(authStore.hasRole("cra")).toBe(true);

    // Set Super Admin
    authStore.rawRoles = ["Super Admin"];
    expect(authStore.isCrc).toBe(true);
    expect(authStore.hasRole("sponsor_designer")).toBe(true);
  });

  it("clinicalStore.subjectIds exposes reactive array of all subject IDs", () => {
    const store = useClinicalStore();
    expect(store.subjectIds).toBeDefined();
    expect(store.subjectIds).toContain("SUBJ-001");
    expect(store.subjectIds).toContain("SUBJ-002");
  });

  it("clinicalStore.screenSubject calls executionService, updates subject status, and writes ledger block", async () => {
    const store = useClinicalStore();

    vi.mocked(executionService.screenSubject).mockResolvedValueOnce({
      eligible: true,
      failed_criteria: [],
      indeterminate_criteria: [],
      criterion_evaluations: [],
    });

    const result = await store.screenSubject("SUBJ-SCREEN-1", { study_id: "CADENCE-101" });

    expect(executionService.screenSubject).toHaveBeenCalledWith("SUBJ-SCREEN-1", { study_id: "CADENCE-101" });
    expect(result.eligible).toBe(true);

    const subject = store.subjects.find((s) => s.id === "SUBJ-SCREEN-1");
    expect(subject).toBeDefined();
    expect(subject?.status).toBe("SCREENED");
    expect(subject?.consentColor).toBe("green");

    // Verify ledger block was added
    const screenBlock = store.ledgerBlocks.find((b) => b.action === "SUBJECT_SCREEN");
    expect(screenBlock).toBeDefined();
    expect((screenBlock?.details as any)?.subject_id).toBe("SUBJ-SCREEN-1");
    expect((screenBlock?.details as any)?.eligible).toBe(true);
  });

  it("clinicalStore.screenSubject handles ineligible result and sets SCREEN_FAILED status", async () => {
    const store = useClinicalStore();

    vi.mocked(executionService.screenSubject).mockResolvedValueOnce({
      eligible: false,
      failed_criteria: ["CRIT-01"],
      indeterminate_criteria: [],
      criterion_evaluations: [],
    });

    const result = await store.screenSubject("SUBJ-SCREEN-2", { study_id: "CADENCE-101" });

    expect(result.eligible).toBe(false);

    const subject = store.subjects.find((s) => s.id === "SUBJ-SCREEN-2");
    expect(subject?.status).toBe("SCREEN_FAILED");
    expect(subject?.consentColor).toBe("red");
  });
});
