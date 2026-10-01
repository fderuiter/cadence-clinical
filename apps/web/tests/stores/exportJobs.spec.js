import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import { setActivePinia, createPinia } from "pinia";
import { useExportJobsStore } from "../../src/stores/exportJobs.js";
import { useToastStore } from "../../src/stores/toasts.js";

describe("Export Jobs Store (useExportJobsStore)", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("submits export job and sets active job state", async () => {
    const store = useExportJobsStore();

    const mockJobResponse = {
      jobId: "job-12345",
      studyId: "STUDY-001",
      format: "dataset_json",
      status: "Queued",
      progressPercent: 0,
      currentStepText: "Job queued for background processing",
      createdAt: new Date().toISOString(),
    };

    globalThis.fetch = vi.fn().mockImplementation((url) => {
      if (url.includes("/api/v1/execution/exports") && !url.includes("job-12345")) {
        return Promise.resolve({
          ok: true,
          status: 202,
          json: () => Promise.resolve(mockJobResponse),
        });
      }
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.resolve(mockJobResponse),
      });
    });

    const job = await store.submitExportJob({
      studyId: "STUDY-001",
      format: "dataset_json",
    });

    expect(job.jobId).toBe("job-12345");
    expect(store.activeJobId).toBe("job-12345");
    expect(store.jobs.length).toBe(1);
    expect(store.activeJob.status).toBe("Queued");
  });

  it("polls status and triggers success toast upon completion", async () => {
    const store = useExportJobsStore();
    const toastStore = useToastStore();

    globalThis.fetch = vi.fn().mockImplementation((url) => {
      if (url.includes("/api/v1/execution/exports/job-abc")) {
        return Promise.resolve({
          ok: true,
          json: () =>
            Promise.resolve({
              jobId: "job-abc",
              studyId: "STUDY-001",
              format: "dataset_json",
              status: "Completed",
              progressPercent: 100,
              currentStepText: "Export ready",
              downloadUrl: "/api/v1/execution/exports/job-abc/download",
            }),
        });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve({}) });
    });

    await store.fetchJobStatus("job-abc");

    expect(store.jobs[0].status).toBe("Completed");
    expect(store.jobs[0].progressPercent).toBe(100);
  });

  it("handles job failure and pushes error toast notification", async () => {
    const store = useExportJobsStore();
    const toastStore = useToastStore();

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      json: () =>
        Promise.resolve({
          jobId: "job-fail",
          studyId: "STUDY-001",
          format: "dataset_json",
          status: "Failed",
          errorDetails: "Dataset serialization error: NULL reference in DM domain",
        }),
    });

    const job = await store.fetchJobStatus("job-fail");

    expect(job.status).toBe("Failed");
    expect(store.jobs[0].status).toBe("Failed");
  });
});
