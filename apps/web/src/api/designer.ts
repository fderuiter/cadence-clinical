import { openApiClient, unwrapResponse } from "./client";
import type { paths } from "./types";

export type StudyResponse =
  paths["/api/v1/studies/{study_id}"]["get"]["responses"][200]["content"]["application/json"];
export type CreateRuleRequest =
  paths["/api/v1/studies/{study_id}/rules"]["post"]["requestBody"]["content"]["application/json"];
export type RuleResponse =
  paths["/api/v1/studies/{study_id}/rules"]["get"]["responses"][200]["content"]["application/json"];
export type ConceptResponse =
  paths["/api/v1/mdr/concepts"]["get"]["responses"][200]["content"]["application/json"];

export type AmendmentBranchPayload =
  paths["/api/v1/designer/amendments/branch"]["post"]["requestBody"]["content"]["application/json"];
export type AmendmentDiffPayload =
  paths["/api/v1/designer/amendments/diff"]["post"]["requestBody"]["content"]["application/json"];
export type AmendmentImpactPayload =
  paths["/api/v1/designer/amendments/impact"]["post"]["requestBody"]["content"]["application/json"];

export type CreateStudyVersionBody =
  paths["/api/v1/studies/{study_id}/versions"]["post"]["requestBody"]["content"]["application/json"];

/**
 * Service module for the Designer microservice (MDR/SDR).
 * Interfaces with study design, CDISC USDM modeling, and metadata rule authoring.
 */
export const designerService = {
  /**
   * Fetches the details of a study design.
   */
  getStudy(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/studies/{study_id}", {
        params: { path: { study_id: studyId } },
        ...options,
      })
    );
  },

  /**
   * Creates a new study version.
   */
  createStudyVersion(
    studyId: string,
    body: CreateStudyVersionBody,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/studies/{study_id}/versions", {
        params: { path: { study_id: studyId } },
        body,
        ...options,
      })
    );
  },

  /**
   * Fetches active metadata rules for a study.
   */
  getRules(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/studies/{study_id}/rules", {
        params: { path: { study_id: studyId } },
        ...options,
      })
    );
  },

  /**
   * Creates/adds an authoring metadata rule for a study.
   */
  createRule(
    studyId: string,
    rule: CreateRuleRequest,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/studies/{study_id}/rules", {
        params: { path: { study_id: studyId } },
        body: rule,
        ...options,
      })
    );
  },

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

  /**
   * Computes quantitative Amendment Impact Summary.
   */
  fetchAmendmentImpact(
    payload: AmendmentImpactPayload,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/designer/amendments/impact", {
        body: payload,
        ...options,
      })
    );
  },

  /**
   * Fetches list of concepts from the MDR global library.
   */
  getConcepts(options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/mdr/concepts", {
        ...options,
      })
    );
  },
};
