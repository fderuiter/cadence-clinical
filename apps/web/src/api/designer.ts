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
   * Fetches list of concepts from the MDR global library.
   */
  getConcepts(options: any = {}): Promise<ConceptResponse[]> {
    return apiClient.get(`/api/v1/mdr/concepts`, options);
  },
};
