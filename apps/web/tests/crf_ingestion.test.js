import { describe, it, expect, vi, beforeEach } from "vitest";

// ingestionClient uses openapi-fetch (src/api/client.ts) for JSON calls and plain fetch for the
// multipart upload. Stub the global fetch before any import so no test reaches the real network.
const { mockFetch } = vi.hoisted(() => {
  const mockFetch = vi.fn();
  vi.stubGlobal("fetch", mockFetch);
  return { mockFetch };
});

import { useClinicalStore } from "../src/stores/clinical";
import { ingestionClient } from "../src/api/ingestionClient";
import { createPinia, setActivePinia } from "pinia";
import { jsonResponse, recordedRequest } from "./fetch_mock.js";

describe("Protocol Ingestion / CRF Builder Frontend Tests", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
    mockFetch.mockReset();
  });

  describe("ingestionClient.js API Methods", () => {
    it("uploadProtocol constructs FormData and includes authorization and reasoning headers", async () => {
      const mockResponse = { id: "cand_123", status: "PENDING_REVIEW" };
      mockFetch.mockResolvedValueOnce(jsonResponse(mockResponse));

      const file = new File(["dummy protocol pdf"], "protocol.pdf", {
        type: "application/pdf",
      });
      const result = await ingestionClient.uploadProtocol(file, {
        changeReason: "Ingesting trial blueprint",
      });

      expect(mockFetch).toHaveBeenCalledTimes(1);
      const [url, options] = mockFetch.mock.calls[0];
      expect(url).toContain("/api/v1/designer/ingestion/upload");
      expect(options.method).toBe("POST");
      expect(options.headers["X-Change-Reason"]).toBe(
        "Ingesting trial blueprint"
      );
      expect(options.body).toBeInstanceOf(FormData);
      expect(result).toEqual(mockResponse);
    });

    it("transitionItem posts status transition with a mandatory reason", async () => {
      mockFetch.mockResolvedValueOnce(
        jsonResponse({ id: "cand_123", status: "PENDING_REVIEW" })
      );

      const result = await ingestionClient.transitionItem(
        "cand_123",
        "cand_visit_1",
        "ACCEPTED",
        "Valid study milestone"
      );

      expect(mockFetch).toHaveBeenCalledTimes(1);
      const { url, method, headers, body } = await recordedRequest(
        mockFetch,
        0
      );
      expect(method).toBe("POST");
      expect(new URL(url).pathname).toBe(
        "/api/v1/designer/ingestion/candidates/cand_123/items/cand_visit_1/transition"
      );
      expect(body).toEqual({
        status: "ACCEPTED",
        reason: "Valid study milestone",
      });
      expect(headers.get("X-Change-Reason")).toBe("Review candidate item");
      expect(result).toEqual({ id: "cand_123", status: "PENDING_REVIEW" });
    });

    it("promoteCandidate submits change reason to promote the draft", async () => {
      mockFetch.mockResolvedValueOnce(
        jsonResponse({ status: "PROMOTED", version_id: "ver_draft_1" })
      );

      const result = await ingestionClient.promoteCandidate(
        "cand_123",
        "Clinical setup promotion"
      );

      expect(mockFetch).toHaveBeenCalledTimes(1);
      const { url, method, headers, body } = await recordedRequest(
        mockFetch,
        0
      );
      expect(method).toBe("POST");
      expect(new URL(url).pathname).toBe(
        "/api/v1/designer/ingestion/candidates/cand_123/promote"
      );
      expect(body).toEqual({ change_reason: "Clinical setup promotion" });
      expect(headers.get("X-Change-Reason")).toBe("Clinical setup promotion");
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
