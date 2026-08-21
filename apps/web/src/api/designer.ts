import { apiClient } from "./apiClient";
import type { components } from "./types";

export type StudyResponse = any; // Or components["schemas"]["Designer_StudyResponse"]
export type CreateRuleRequest =
  components["schemas"]["Designer_CreateRuleRequest"];
export type RuleResponse = any;
export type ConceptResponse = any;

/**
 * Service module for the Designer microservice (MDR/SDR).
 * Interfaces with study design, CDISC USDM modeling, and metadata rule authoring.
 */
export const designerService = {
  /**
   * Fetches the details of a study design.
   */
  getStudy(studyId: string, options: any = {}): Promise<StudyResponse> {
    return apiClient.get(`/api/v1/studies/${studyId}`, options);
  },

  /**
   * Creates a new study version.
   */
  createStudyVersion(
    studyId: string,
    body: any,
    options: any = {}
  ): Promise<any> {
    return apiClient.post(`/api/v1/studies/${studyId}/versions`, body, options);
  },

  /**
   * Fetches active metadata rules for a study.
   */
  getRules(studyId: string, options: any = {}): Promise<RuleResponse[]> {
    return apiClient.get(`/api/v1/studies/${studyId}/rules`, options);
  },

  /**
   * Creates/adds an authoring metadata rule for a study.
   */
  createRule(
    studyId: string,
    rule: CreateRuleRequest,
    options: any = {}
  ): Promise<RuleResponse> {
    return apiClient.post(`/api/v1/studies/${studyId}/rules`, rule, options);
  },

  /**
   * Creates an isolated, immutable protocol amendment working branch.
   */
  createAmendmentBranch(
    payload: {
      study_id: string;
      base_version_tag: string;
      amendment_type?: string;
      requires_reconsent?: boolean;
      change_reason: string;
      branch_name?: string;
    },
    options: any = {}
  ): Promise<any> {
    return apiClient.post(
      `/api/v1/designer/amendments/branch`,
      payload,
      options
    );
  },

  /**
   * Computes multi-layer visual and semantic protocol diff.
   */
  fetchAmendmentDiff(
    payload: {
      study_id: string;
      base_version_tag: string;
      amended_version_tag: string;
      base_payload?: any;
      draft_payload?: any;
    },
    options: any = {}
  ): Promise<any> {
    return apiClient.post(`/api/v1/designer/amendments/diff`, payload, options);
  },

  /**
   * Computes quantitative Amendment Impact Summary.
   */
  fetchAmendmentImpact(
    payload: {
      study_id: string;
      base_version_tag: string;
      amended_version_tag: string;
    },
    options: any = {}
  ): Promise<any> {
    return apiClient.post(
      `/api/v1/designer/amendments/impact`,
      payload,
      options
    );
  },

  /**
   * Fetches list of concepts from the MDR global library.
   */
  getConcepts(options: any = {}): Promise<ConceptResponse[]> {
    return apiClient.get(`/api/v1/mdr/concepts`, options);
  },
};
