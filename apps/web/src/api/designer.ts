import { unwrapResponse } from "./client";
import { openApiClient } from "./legacyClient";
import type { paths } from "./types";

export type AmendmentBranchPayload =
  paths["/api/v1/designer/amendments/branch"]["post"]["requestBody"]["content"]["application/json"];
export type AmendmentDiffPayload =
  paths["/api/v1/designer/amendments/diff"]["post"]["requestBody"]["content"]["application/json"];

/**
 * Service module for the Designer microservice (MDR/SDR).
 * Interfaces with study design, CDISC USDM modeling, and metadata rule authoring.
 */
export const designerService = {
  /**
   * Creates an isolated, immutable protocol amendment working branch.
   */
  createAmendmentBranch(
    payload: AmendmentBranchPayload,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/designer/amendments/branch", {
        body: payload,
        ...options,
      })
    );
  },

  /**
   * Computes multi-layer visual and semantic protocol diff.
   */
  fetchAmendmentDiff(
    payload: AmendmentDiffPayload,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/designer/amendments/diff", {
        body: payload,
        ...options,
      })
    );
  },
};
