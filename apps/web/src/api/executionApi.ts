import { encodeChangeReason } from "./changeReason";
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
export type Observation = Schemas["Execution_ObservationResponse"];
export type FormSubmission = Schemas["Execution_FormSubmissionResponse"];
export type ClinicalQuery = Schemas["Execution_ClinicalQueryResponse"];

export interface RaiseQuery {
  studyId: string;
  subjectCode: string;
  observation: Observation;
  explanation: string;
}

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

  /** A participant's observations (GET /api/v1/execution/observations/subject/{studyId}/{subjectId}), by subject code. */
  listObservations(
    studyId: string,
    subjectCode: string
  ): Promise<Observation[]> {
    return unwrapResponse(
      client.GET(
        "/api/v1/execution/observations/subject/{studyId}/{subjectId}",
        {
          params: { path: { studyId, subjectId: subjectCode } },
        }
      )
    );
  },

  /** A participant's form submissions (GET /api/v1/execution/forms/subject/{studyId}/{subjectId}), by subject code. */
  listFormSubmissions(
    studyId: string,
    subjectCode: string
  ): Promise<FormSubmission[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/forms/subject/{studyId}/{subjectId}", {
        params: { path: { studyId, subjectId: subjectCode } },
      })
    );
  },

  /** Records that the signed-in user verified an observation against its source. Execution records who and when. */
  verifyObservationSdv(id: string): Promise<Observation> {
    return unwrapResponse(
      client.POST("/api/v1/execution/observations/{id}/sdv", {
        params: { path: { id } },
      })
    );
  },

  /** Flags an observation for re-verification, clearing any earlier verification; the reason is recorded. */
  flagObservationSdv(id: string, reason: string): Promise<Observation> {
    return unwrapResponse(
      client.POST("/api/v1/execution/observations/{id}/sdv/flag", {
        params: { path: { id } },
        body: { reason },
      })
    );
  },

  /** Queries on a participant (GET /api/v1/execution/queries?study_id=&subject_id=), by subject code. */
  listQueries(studyId: string, subjectCode: string): Promise<ClinicalQuery[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/queries", {
        params: { query: { study_id: studyId, subject_id: subjectCode } },
      })
    );
  },

  /** Raises a query on one observation. Execution records the signed-in user as its author. */
  raiseQuery({
    studyId,
    subjectCode,
    observation,
    explanation,
  }: RaiseQuery): Promise<ClinicalQuery> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries", {
        body: {
          study_id: studyId,
          subject_id: subjectCode,
          test_code: observation.test_code,
          domain: observation.domain,
          observation_id: observation.id,
          visit_id: observation.visit_id ?? null,
          site_id: observation.site_id ?? null,
          explanation,
          // Execution's own defaults for a query a person raises.
          status: "OPEN",
          origin: "MANUAL",
          priority: "MEDIUM",
        },
      })
    );
  },

  /**
   * Answers an open or reopened query. The responder is the signed-in user; this client never sends a
   * responder of its own (#4920).
   */
  respondQuery(id: string, response: string): Promise<ClinicalQuery> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/{id}/respond", {
        params: { path: { id } },
        body: { response },
      })
    );
  },

  /** Closes an answered query (CRA or data manager), with the reason as X-Change-Reason. */
  closeQuery(id: string, reason: string): Promise<ClinicalQuery> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/{id}/close", {
        params: { path: { id } },
        headers: { "X-Change-Reason": reason },
      })
    );
  },

  /** Reopens an answered or closed query (CRA or data manager); Execution requires the reason in both places. */
  reopenQuery(id: string, reason: string): Promise<ClinicalQuery> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/{id}/reopen", {
        params: { path: { id } },
        body: { reason },
        headers: { "X-Change-Reason": reason },
      })
    );
  },

  /** Cancels a query that is not closed (CRA or data manager); Execution requires the reason in both places. */
  cancelQuery(id: string, reason: string): Promise<ClinicalQuery> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/{id}/cancel", {
        params: { path: { id } },
        body: { reason },
        headers: { "X-Change-Reason": reason },
      })
    );
  },

  /**
   * Activates a site for the study. Execution refuses (400) unless eTMF reports the site's activation
   * documents complete, and audits the attempt either way with the reason sent as X-Change-Reason
   * (encoded per ADR-2495, so any script reaches the audit trail as typed).
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
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },
};
