import { openApiClient, unwrapResponse, getBaseUrl } from "./client";
import { useAuthStore } from "../stores/auth";
import type { paths } from "./types";

export interface IngestionOptions {
  changeReason?: string;
}

/**
 * API Client for Protocol Ingestion / CRF Builder (Phase 2 Ingestion).
 */
export const ingestionClient = {
  /**
   * Uploads a PDF/DOCX protocol document and triggers candidate draft generation.
   */
  async uploadProtocol(file: File, options: IngestionOptions = {}) {
    const { changeReason = "Upload protocol document" } = options;
    const baseUrl = getBaseUrl();
    const url = `${baseUrl}/api/v1/designer/ingestion/upload`;

    const formData = new FormData();
    formData.append("file", file);

    const headers: Record<string, string> = {};
    try {
      const authStore = useAuthStore();
      const token = authStore?.token || authStore?.accessToken;
      if (token) {
        headers["Authorization"] = `Bearer ${token}`;
      }
    } catch {
      // Pinia inactive
    }
    if (changeReason) {
      headers["X-Change-Reason"] = changeReason;
    }

    const response = await fetch(url, {
      method: "POST",
      headers,
      body: formData,
    });

    if (!response.ok) {
      const data = (await response.json().catch(() => ({}))) as Record<string, unknown>;
      throw new Error((data?.detail as string) || "Upload failed");
    }
    return response.json();
  },

  /**
   * Retrieves the status of an ingestion job.
   */
  getJobStatus(jobId: string, options: IngestionOptions = {}) {
    const { changeReason = "Get job status" } = options;
    return unwrapResponse(
      openApiClient.GET("/api/v1/designer/ingestion/jobs/{job_id}", {
        params: { path: { job_id: jobId } },
        headers: { "X-Change-Reason": changeReason },
      })
    );
  },

  /**
   * Retrieves a review-only candidate draft.
   */
  getCandidate(candidateId: string, options: IngestionOptions = {}) {
    const { changeReason = "Get candidate draft" } = options;
    return unwrapResponse(
      openApiClient.GET("/api/v1/designer/ingestion/candidates/{candidate_id}", {
        params: { path: { candidate_id: candidateId } },
        headers: { "X-Change-Reason": changeReason },
      })
    );
  },

  /**
   * Transitions an item's review status with a mandatory change justification reason.
   */
  transitionItem(
    candidateId: string,
    itemId: string,
    status: string,
    reason: string,
    updatedFields: Record<string, unknown> = {},
    options: IngestionOptions = {}
  ) {
    const { changeReason = "Review candidate item" } = options;
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition",
        {
          params: {
            path: {
              candidate_id: candidateId,
              item_id: itemId,
            },
          },
          body: {
            status,
            reason,
            ...updatedFields,
          } as paths["/api/v1/designer/ingestion/candidates/{candidate_id}/items/{item_id}/transition"]["post"]["requestBody"]["content"]["application/json"],
          headers: { "X-Change-Reason": changeReason },
        }
      )
    );
  },

  /**
   * Promotes the reviewed candidate items to a non-published DRAFT version.
   */
  promoteCandidate(candidateId: string, changeReason: string) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/designer/ingestion/candidates/{candidate_id}/promote",
        {
          params: { path: { candidate_id: candidateId } },
          body: { change_reason: changeReason },
          headers: { "X-Change-Reason": changeReason },
        }
      )
    );
  },
};
