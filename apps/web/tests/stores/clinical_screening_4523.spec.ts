import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../../src/stores/clinical";
import { useToastStore } from "../../src/stores/toasts.js";
import { executionService } from "../../src/api/execution";

vi.mock("../../src/api/execution", () => ({
  executionService: {
    createSubject: vi.fn(),
    screenSubject: vi.fn(),
    listLabAlerts: vi.fn().mockResolvedValue([]),
  },
}));

function httpError(status: number) {
  const err = new Error(`Request failed with status ${status}`) as Error & {
    status: number;
  };
  err.status = status;
  return err;
}

// #4523: a failed screening call must never be recorded as a screening outcome.
describe("clinicalStore.screenSubject API failure handling (#4523)", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    window.localStorage.clear();
  });

  it.each([500, 502, 504])(
    "HTTP %i leaves subjects, ledger and localStorage untouched and shows an error toast",
    async (status) => {
      const store = useClinicalStore();
      const toasts = useToastStore();
      const subjectsBefore = JSON.stringify(store.subjects);
      const ledgerBefore = store.ledgerBlocks.length;
      vi.mocked(executionService.screenSubject).mockRejectedValueOnce(
        httpError(status)
      );

      await expect(
        store.screenSubject("SUBJ-001", { study_id: "CADENCE-101" })
      ).rejects.toMatchObject({ status });

      expect(JSON.stringify(store.subjects)).toBe(subjectsBefore);
      expect(store.ledgerBlocks.length).toBe(ledgerBefore);
      expect(
        store.ledgerBlocks.some((b) => b.action === "SUBJECT_SCREEN")
      ).toBe(false);
      expect(window.localStorage.getItem("cadence_clinical_subjects")).toBe(
        null
      );
      expect(toasts.toasts).toHaveLength(1);
      expect(toasts.toasts[0]).toMatchObject({
        tone: "error",
        title: "Subject screening failed",
      });
      expect(toasts.toasts[0].message).toContain(`HTTP ${status}`);
    }
  );

  it("does not create a placeholder subject for an unknown ID when screening fails", async () => {
    const store = useClinicalStore();
    vi.mocked(executionService.screenSubject).mockRejectedValueOnce(
      httpError(502)
    );

    await expect(store.screenSubject("SUBJ-NEW-4523")).rejects.toThrow();

    expect(store.subjects.find((s) => s.id === "SUBJ-NEW-4523")).toBe(
      undefined
    );
  });

  it("treats an empty response as a failure instead of defaulting to eligible", async () => {
    const store = useClinicalStore();
    const toasts = useToastStore();
    vi.mocked(executionService.screenSubject).mockResolvedValueOnce(
      undefined as never
    );

    await expect(store.screenSubject("SUBJ-001")).rejects.toThrow();

    expect(store.subjects.find((s) => s.id === "SUBJ-001")?.status).not.toBe(
      "SCREENED"
    );
    expect(store.ledgerBlocks.some((b) => b.action === "SUBJECT_SCREEN")).toBe(
      false
    );
    expect(toasts.toasts[0]?.tone).toBe("error");
  });

  it("still records a successful screening", async () => {
    const store = useClinicalStore();
    const toasts = useToastStore();
    vi.mocked(executionService.screenSubject).mockResolvedValueOnce({
      eligible: true,
      failed_criteria: [],
      indeterminate_criteria: [],
      criterion_evaluations: [],
    });

    await store.screenSubject("SUBJ-001");

    expect(store.subjects.find((s) => s.id === "SUBJ-001")?.status).toBe(
      "SCREENED"
    );
    expect(store.ledgerBlocks.some((b) => b.action === "SUBJECT_SCREEN")).toBe(
      true
    );
    expect(toasts.toasts).toHaveLength(0);
  });
});
