import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/execution";

/**
 * Typed client for the .NET Execution service, generated from docs/openapi/execution_openapi.json.
 *
 * Screens move onto this client one at a time (docs/web/API_MIGRATION.md); `execution.ts` is the
 * Python-era client it replaces.
 */
type Schemas = components["schemas"];

export type SubjectSummary = Schemas["Execution_SubjectSummaryResponse"];
export type SubjectList = Schemas["Execution_SubjectListResponse"];
export type ComplianceStatus = Schemas["Execution_ComplianceStatusResponse"];
export type OperationEnvelope = Schemas["Execution_OperationEnvelopeResponse"];

export interface ListSubjectsQuery {
  studyId: string;
  siteId?: string;
  status?: string;
  limit?: number;
  offset?: number;
}

const client = createOpenApiClient<paths>();

export const executionApi = {
  /** One page of a study's subjects in enrollment order (GET /api/v1/execution/subjects). */
  listSubjects(query: ListSubjectsQuery): Promise<SubjectList> {
    return unwrapResponse(
      client.GET("/api/v1/execution/subjects", {
        params: {
          query: {
            study_id: query.studyId,
            site_id: query.siteId,
            status: query.status,
            limit: query.limit,
            offset: query.offset,
          },
        },
      })
    );
  },

  /**
   * Whether eTMF has reported the site's documents complete for a milestone (default SITE_ACTIVATION).
   * The service answers even when eTMF has reported nothing: `is_complete` is false and no documents are listed.
   */
  getSiteComplianceStatus(
    siteId: string,
    studyId: string,
    milestone?: string
  ): Promise<ComplianceStatus> {
    return unwrapResponse(
      client.GET("/api/v1/execution/sites/{siteId}/compliance-status", {
        params: {
          path: { siteId },
          query: { study_id: studyId, milestone },
        },
      })
    );
  },

  /**
   * Activates a site for the study. Execution refuses (400) unless eTMF reports the site's activation
   * documents complete, and audits the attempt either way with the reason sent as X-Change-Reason.
   */
  activateSite(
    siteId: string,
    studyId: string,
    changeReason: string
  ): Promise<OperationEnvelope> {
    return unwrapResponse(
      client.POST("/api/v1/execution/sites/{siteId}/activate", {
        params: { path: { siteId } },
        body: { study_id: studyId },
        headers: { "X-Change-Reason": changeReason },
      })
    );
  },
};
