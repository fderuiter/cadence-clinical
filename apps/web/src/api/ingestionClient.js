import { apiClient, getBaseUrl } from "./apiClient";
import { useAuthStore } from "../stores/auth";

/**
 * API Client for Protocol Ingestion / CRF Builder (Phase 2 Ingestion).
 */
export const ingestionClient = {
  /**
   * Uploads a PDF/DOCX protocol document and triggers candidate draft generation.
   */
  async uploadProtocol(file, options = {}) {
    const { changeReason = "Upload protocol document" } = options;
    const baseUrl = getBaseUrl();
    const url = `${baseUrl}/api/v1/designer/ingestion/upload`;

    const formData = new FormData();
    formData.append("file", file);

    const headers = {};
    const authStore = useAuthStore();
    const token = authStore?.token || authStore?.accessToken;
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
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
      const data = await response.json().catch(() => ({}));
      throw new Error(data?.detail || "Upload failed");
    }
    return response.json();
  },

  /**
   * Retrieves the status of an ingestion job.
   */
  async getJobStatus(jobId, options = {}) {
    const { changeReason = "Get job status" } = options;
    return apiClient.get(`/api/v1/designer/ingestion/jobs/${jobId}`, {
      changeReason,
    });
  },

  /**
   * Retrieves a review-only candidate draft.
   */
  async getCandidate(candidateId, options = {}) {
    const { changeReason = "Get candidate draft" } = options;
    return apiClient.get(
      `/api/v1/designer/ingestion/candidates/${candidateId}`,
      { changeReason }
    );
  },

  /**
   * Transitions an item's review status with a mandatory change justification reason.
   */
  async transitionItem(
    candidateId,
    itemId,
    status,
    reason,
    updatedFields = {},
    options = {}
  ) {
    const { changeReason = "Review candidate item" } = options;
    return apiClient.post(
      `/api/v1/designer/ingestion/candidates/${candidateId}/items/${itemId}/transition`,
      { status, reason, ...updatedFields },
      { changeReason }
    );
  },

  /**
   * Promotes the reviewed candidate items to a non-published DRAFT version.
   */
  async promoteCandidate(candidateId, changeReason) {
    return apiClient.post(
      `/api/v1/designer/ingestion/candidates/${candidateId}/promote`,
      { change_reason: changeReason },
      { changeReason }
    );
  },
};
