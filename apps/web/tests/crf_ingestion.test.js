import { describe, it, expect, vi, beforeEach } from "vitest";
import { useClinicalStore } from "../src/stores/clinical";
import { ingestionClient } from "../src/api/ingestionClient";
import { createPinia, setActivePinia } from "pinia";

// Mock fetch globally
global.fetch = vi.fn();

// Mock apiClient to prevent actual HTTP calls in store actions
vi.mock("../src/api/apiClient", () => ({
  apiClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
  getBaseUrl: () => "http://localhost:8000",
}));

describe("Protocol Ingestion / CRF Builder Frontend Tests", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
  });

  describe("ingestionClient.js API Methods", () => {
    it("uploadProtocol constructs FormData and includes authorization and reasoning headers", async () => {
      const mockResponse = { id: "cand_123", status: "PENDING_REVIEW" };
      global.fetch.mockResolvedValueOnce({
        ok: true,
        json: async () => mockResponse,
      });

      const file = new File(["dummy protocol pdf"], "protocol.pdf", {
        type: "application/pdf",
      });
      const result = await ingestionClient.uploadProtocol(file, {
        changeReason: "Ingesting trial blueprint",
      });

      expect(global.fetch).toHaveBeenCalledTimes(1);
      const [url, options] = global.fetch.mock.calls[0];
      expect(url).toContain("/api/v1/designer/ingestion/upload");
      expect(options.method).toBe("POST");
      expect(options.headers["X-Change-Reason"]).toBe(
        "Ingesting trial blueprint"
      );
      expect(options.body).toBeInstanceOf(FormData);
      expect(result).toEqual(mockResponse);
    });

    it("transitionItem posts status transition with a mandatory reason", async () => {
      const { apiClient } = await import("../src/api/apiClient");
      apiClient.post.mockResolvedValueOnce({
        id: "cand_123",
        status: "PENDING_REVIEW",
      });

      const result = await ingestionClient.transitionItem(
        "cand_123",
        "cand_visit_1",
        "ACCEPTED",
        "Valid study milestone"
      );

      expect(apiClient.post).toHaveBeenCalledWith(
        "/api/v1/designer/ingestion/candidates/cand_123/items/cand_visit_1/transition",
        { status: "ACCEPTED", reason: "Valid study milestone" },
        { changeReason: "Review candidate item" }
      );
      expect(result).toBeDefined();
    });

    it("promoteCandidate submits change reason to promote the draft", async () => {
      const { apiClient } = await import("../src/api/apiClient");
      apiClient.post.mockResolvedValueOnce({
        status: "PROMOTED",
        version_id: "ver_draft_1",
      });

      const result = await ingestionClient.promoteCandidate(
        "cand_123",
        "Clinical setup promotion"
      );

      expect(apiClient.post).toHaveBeenCalledWith(
        "/api/v1/designer/ingestion/candidates/cand_123/promote",
        { change_reason: "Clinical setup promotion" },
        { changeReason: "Clinical setup promotion" }
      );
      expect(result.status).toBe("PROMOTED");
    });
  });

  describe("Pinia clinical.js Ingestion Store Actions", () => {
    it("uploadProtocolDocument calls API, populates candidateDraft and tracks ingestionJobs", async () => {
      const store = useClinicalStore();
      const mockCandidate = {
        id: "cand_xyz",
        status: "PENDING_REVIEW",
        items: {
          cand_visit_1: { id: "cand_visit_1", review_status: "PENDING" },
        },
      };

      vi.spyOn(ingestionClient, "uploadProtocol").mockResolvedValueOnce(
        mockCandidate
      );

      const file = new File(["dummy docx"], "protocol.docx");
      await store.uploadProtocolDocument(file, "Store upload test");

      expect(store.candidateDraft).toEqual(mockCandidate);
      expect(store.ingestionJobs).toHaveLength(1);
      expect(store.ingestionJobs[0].job_id).toBe("cand_xyz");
      expect(store.ingestionLoading).toBe(false);
    });

    it("transitionCandidateItemState transitions candidate items correctly", async () => {
      const store = useClinicalStore();
      const mockCandidate = {
        id: "cand_xyz",
        items: {
          cand_visit_1: { id: "cand_visit_1", review_status: "PENDING" },
        },
      };
      store.candidateDraft = mockCandidate;

      const mockUpdatedCandidate = {
        ...mockCandidate,
        items: {
          cand_visit_1: {
            id: "cand_visit_1",
            review_status: "ACCEPTED",
            reason: "Approved visit",
          },
        },
      };

      vi.spyOn(ingestionClient, "transitionItem").mockResolvedValueOnce(
        mockUpdatedCandidate
      );

      await store.transitionCandidateItemState(
        "cand_xyz",
        "cand_visit_1",
        "ACCEPTED",
        "Approved visit"
      );

      expect(store.candidateDraft).toEqual(mockUpdatedCandidate);
      expect(store.candidateDraft.items.cand_visit_1.review_status).toBe(
        "ACCEPTED"
      );
    });

    it("promoteCandidateDraft transitions store draft status to PROMOTED", async () => {
      const store = useClinicalStore();
      store.candidateDraft = { id: "cand_xyz", status: "PENDING_REVIEW" };

      vi.spyOn(ingestionClient, "promoteCandidate").mockResolvedValueOnce({
        status: "PROMOTED",
        version_id: "ver_draft_1",
      });

      const res = await store.promoteCandidateDraft(
        "cand_xyz",
        "Promoting from store action"
      );

      expect(res.status).toBe("PROMOTED");
      expect(store.candidateDraft.status).toBe("PROMOTED");
    });
  });
});
