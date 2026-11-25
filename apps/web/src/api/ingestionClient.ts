import { encodeChangeReason } from "./changeReason";
import { ApiError, unwrapResponse, getBaseUrl } from "./client";
import { openApiClient } from "./legacyClient";
import {
  applyDemoIdentityHeaders,
  fetchWithSingleFlightRetry,
  getBearerToken,
  notifySubscriptionExpiredIfNeeded,
} from "./authorization.js";
import type { paths } from "./types";
import { isDemoBuild } from "../utils/demoMode";

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
    const token = await getBearerToken();
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    }
    if (isDemoBuild()) {
      applyDemoIdentityHeaders(headers);
    }
    if (changeReason) {
      headers["X-Change-Reason"] = encodeChangeReason(changeReason);
    }

    const response = await fetchWithSingleFlightRetry(url, {
      method: "POST",
      headers,
      body: formData,
    });

    if (!response.ok) {
      const data = (await response.json().catch(() => null)) as Record<
        string,
        unknown
      > | null;
      const expiredGuidance = notifySubscriptionExpiredIfNeeded(
        response.status,
        data
      );
      throw new ApiError(
        expiredGuidance || (data?.detail as string) || "Upload failed",
        response.status,
        response.statusText,
        data
      );
    }
    return response.json();
  },

  /**
   * Retrieves a review-only candidate draft.
   */
  getCandidate(candidateId: string, options: IngestionOptions = {}) {
    const { changeReason = "Get candidate draft" } = options;
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/designer/ingestion/candidates/{candidate_id}",
        {
          params: { path: { candidate_id: candidateId } },
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
        }
      )
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
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
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
          headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
        }
      )
    );
  },
};
