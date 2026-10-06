import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/execution";

/** The reason for change recorded when a monitor verifies an observation against its source. */
export const SDV_VERIFY_REASON = "Verified against source documents";

/**
 * Typed client for the .NET Execution service, generated from docs/openapi/execution_openapi.json.
 *
 * Every Execution call in the web app goes through this client (docs/web/API_MIGRATION.md).
 */
type Schemas = components["schemas"];

export type SubjectSummary = Schemas["Execution_SubjectSummaryResponse"];
export type SubjectList = Schemas["Execution_SubjectListResponse"];
export type Subject = Schemas["Execution_SubjectResponse"];
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

export interface AddSubject {
  studyId: string;
  subjectCode: string;
  siteId?: string;
  reason: string;
}

export interface CreateFormSubmission {
  studyId: string;
  subjectId: string;
  formId: string;
  siteId?: string | null;
  visitId?: string | null;
  protocolVersion?: string | null;
  payload?: string | null;
}

export interface ListSubjectsQuery {
  studyId: string;
  siteId?: string;
  status?: string;
  limit?: number;
  offset?: number;
}

export interface WeeklyEnrollmentProjection {
  weekIndex: number;
  weekStartDate: string;
  projectedCumulativeEnrolled: number;
  lowerBoundCI: number;
  upperBoundCI: number;
  targetCumulative: number;
  weeklyRate: number;
}

export interface SiteEnrollmentVelocity {
  siteId: string;
  siteName: string;
  activeSubjects: number;
  historicalWeeklyRate: number;
  projectedWeeklyRate: number;
  velocityRiskLevel: string;
}

export interface EnrollmentVelocityParams {
  studyId?: string;
  targetSubjects: number;
  targetCompletionDate: string;
  historicalWeeklyRate: number;
  currentlyEnrolled: number;
  activeSiteCount: number;
  simulationHorizonWeeks: number;
}

export interface EnrollmentVelocityForecastResult {
  studyId: string;
  currentVelocityWeekly: number;
  targetVelocityWeekly: number;
  status: "ON_TRACK" | "AT_RISK" | "BEHIND" | "AHEAD" | string;
  forecastedCompletionDate: string;
  daysAheadOrBehind: number;
  targetSubjects: number;
  currentlyEnrolled: number;
  weeklyProjections: WeeklyEnrollmentProjection[];
  siteVelocities: SiteEnrollmentVelocity[];
}

export interface SiteRiskCategoryScore {
  categoryKey: string;
  categoryName: string;
  riskScore: number;
  riskTier: "LOW" | "MEDIUM" | "HIGH" | string;
  triggeredMetricsCount: number;
  details: string;
}

export interface SiteRiskHeatmapRow {
  siteId: string;
  siteName: string;
  compositeRiskScore: number;
  riskTier: "LOW" | "MEDIUM" | "HIGH" | string;
  rank: number;
  categoryScores: SiteRiskCategoryScore[];
  activeQueriesCount: number;
  activeDeviationsCount: number;
  recommendedActions: string[];
}

export interface SiteRiskHeatmapMatrix {
  studyId: string;
  evaluatedAtUtc: string;
  totalSites: number;
  highRiskSitesCount: number;
  mediumRiskSitesCount: number;
  lowRiskSitesCount: number;
  categories: string[];
  rows: SiteRiskHeatmapRow[];
}

const client = createOpenApiClient<any>();

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
   * Adds a participant to a study in screening (POST /api/v1/execution/subjects). The canvas sends no
   * demographics and no arm: identity details and randomization are not captured here. The reason reaches
   * the audit entry as X-Change-Reason.
   */
  addSubject({
    studyId,
    subjectCode,
    siteId,
    reason,
  }: AddSubject): Promise<Subject> {
    return unwrapResponse(
      client.POST("/api/v1/execution/subjects", {
        body: {
          subject_id: subjectCode,
          study_id: studyId,
          site_id: siteId || null,
          arm_id: null,
          demographics: null,
        },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /**
   * Moves a participant to another state (PATCH /api/v1/execution/subjects/{subjectId}/state), by id or
   * subject code. Execution checks the transition and, for enrolment, that the site is activated; the reason
   * is sent as X-Change-Reason.
   */
  changeSubjectState(
    subjectId: string,
    state: string,
    reason: string
  ): Promise<Subject> {
    return unwrapResponse(
      client.PATCH("/api/v1/execution/subjects/{subjectId}/state", {
        params: { path: { subjectId } },
        body: { status: state, state: null },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
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

  /** Creates an initial form submission (POST /api/v1/execution/forms). */
  createFormSubmission({
    studyId,
    subjectId,
    formId,
    siteId,
    visitId,
    protocolVersion,
    payload,
  }: CreateFormSubmission): Promise<FormSubmission> {
    return unwrapResponse(
      client.POST("/api/v1/execution/forms", {
        body: {
          study_id: studyId,
          subject_id: subjectId,
          form_id: formId,
          site_id: siteId ?? null,
          visit_id: visitId ?? null,
          protocol_version: protocolVersion ?? null,
          payload: payload ?? null,
        },
      })
    );
  },

  /** Finalizes submission state on backend server (POST /api/v1/execution/forms/{id}/complete). */
  completeFormSubmission(id: string): Promise<FormSubmission> {
    return unwrapResponse(
      client.POST("/api/v1/execution/forms/{id}/complete", {
        params: { path: { id } },
      })
    );
  },

  /**
   * Records that the signed-in user verified an observation against its source. Execution records who and
   * when, refuses (403) anyone but a CRA, monitor or data manager, and requires a reason for change (#4920).
   */
  verifyObservationSdv(
    id: string,
    reason: string = SDV_VERIFY_REASON
  ): Promise<Observation> {
    return unwrapResponse(
      client.POST("/api/v1/execution/observations/{id}/sdv", {
        params: { path: { id } },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /**
   * Flags an observation for re-verification, clearing any earlier verification. The reason is recorded on the
   * observation and sent as the reason for change.
   */
  flagObservationSdv(id: string, reason: string): Promise<Observation> {
    return unwrapResponse(
      client.POST("/api/v1/execution/observations/{id}/sdv/flag", {
        params: { path: { id } },
        body: { reason },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
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
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /** Reopens an answered or closed query (CRA or data manager); Execution requires the reason in both places. */
  reopenQuery(id: string, reason: string): Promise<ClinicalQuery> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/{id}/reopen", {
        params: { path: { id } },
        body: { reason },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /** Cancels a query that is not closed (CRA or data manager); Execution requires the reason in both places. */
  cancelQuery(id: string, reason: string): Promise<ClinicalQuery> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/{id}/cancel", {
        params: { path: { id } },
        body: { reason },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
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

  /** Gets enrollment velocity forecasting for a study. */
  getEnrollmentVelocity(studyId: string): Promise<EnrollmentVelocityForecastResult> {
    return unwrapResponse(
      client.GET("/api/v1/execution/studies/{studyId}/enrollment-velocity", {
        params: { path: { studyId } },
      })
    );
  },

  /** Simulates predictive enrollment velocity with custom scenario parameters. */
  simulateEnrollmentVelocity(
    studyId: string,
    params: Partial<EnrollmentVelocityParams>
  ): Promise<EnrollmentVelocityForecastResult> {
    return unwrapResponse(
      client.POST("/api/v1/execution/studies/{studyId}/enrollment-velocity/forecast", {
        params: { path: { studyId } },
        body: params,
      })
    );
  },

  /** Retrieves site risk heatmap matrix from RBQM quality service. */
  getSiteRiskHeatmap(studyId: string): Promise<SiteRiskHeatmapMatrix> {
    return unwrapResponse(
      client.GET("/api/v1/quality/rbqm/site-risk-heatmaps", {
        params: { query: { study_id: studyId } },
      })
    );
  },

  /** Computes site risk heatmap matrix with updated metrics. */
  computeSiteRiskHeatmap(
    studyId: string,
    siteCodes?: string[]
  ): Promise<SiteRiskHeatmapMatrix> {
    return unwrapResponse(
      client.POST("/api/v1/quality/rbqm/site-risk-heatmaps/compute", {
        params: { query: { study_id: studyId } },
        body: { studyId, siteCodes },
      })
    );
  },
};
