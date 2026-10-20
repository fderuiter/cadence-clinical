import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/execution";

/**
 * Typed client for the .NET Execution service, generated from docs/openapi/execution_openapi.json.
 *
 * Every Execution call in the web app goes through this client (docs/web/API_MIGRATION.md). eCRF forms are not here:
 * they are kernel form instances served by the kernel host (ecrfFormsApi.ts, ADR-2622).
 */
type Schemas = components["schemas"];

export type SubjectSummary = Schemas["Execution_SubjectSummaryResponse"];
export type SubjectList = Schemas["Execution_SubjectListResponse"];
export type Subject = Schemas["Execution_SubjectResponse"];
export type ComplianceStatus = Schemas["Execution_ComplianceStatusResponse"];
export type OperationEnvelope = Schemas["Execution_OperationEnvelopeResponse"];
export type ClinicalQuery = Schemas["Execution_ClinicalQueryResponse"];
export type BatchQueryResponse = Schemas["Execution_BatchQueryResponse"];

export interface AddSubject {
  studyId: string;
  subjectCode: string;
  siteId?: string;
  reason: string;
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

  /** Queries on a participant (GET /api/v1/execution/queries?study_id=&subject_id=), by subject code. */
  listQueries(studyId: string, subjectCode: string): Promise<ClinicalQuery[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/queries", {
        params: { query: { study_id: studyId, subject_id: subjectCode } },
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

  /** Batch closes multiple queries (CRA or data manager), with the reason as X-Change-Reason. */
  batchCloseQueries(
    queryIds: string[],
    reason: string
  ): Promise<BatchQueryResponse> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/batch-close", {
        body: { query_ids: queryIds, reason },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /** Batch cancels multiple queries (CRA or data manager), with the reason as X-Change-Reason. */
  batchCancelQueries(
    queryIds: string[],
    reason: string
  ): Promise<BatchQueryResponse> {
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/batch-cancel", {
        body: { query_ids: queryIds, reason },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

  /** Batch responds to multiple open or reopened queries, with the reason as X-Change-Reason. */
  batchRespondQueries(
    queryIds: string[],
    response: string,
    reason?: string
  ): Promise<BatchQueryResponse> {
    const changeReason = reason || response;
    return unwrapResponse(
      client.POST("/api/v1/execution/queries/batch-respond", {
        body: { query_ids: queryIds, response, reason: changeReason },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
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

  /** Reconciles a batch of IP kit returns atomically in RTSM (POST /api/v1/execution/rtsm/kits/reconcile-batch). */
  async reconcileIpKitBatch(payload: {
    study_id: string;
    site_id: string;
    processed_by: string;
    notes?: string;
    reason_for_change: string;
    items: Array<{
      kit_number: string;
      lot_number: string;
      expected_lot_number?: string;
      residual_volume_ml?: number;
      residual_volume_percentage?: number;
      disposition: string;
      disposition_reason: string;
    }>;
  }): Promise<any> {
    const res = await fetch("/api/v1/execution/rtsm/kits/reconcile-batch", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Change-Reason": encodeChangeReason(payload.reason_for_change),
      },
      body: JSON.stringify(payload),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({ detail: "Request failed" }));
      throw new Error(
        err.detail || "Server failed to process batch reconciliation."
      );
    }
    return res.json();
  },

  /** Retrieves site IP kit inventory catalog (GET /api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}). */
  async getSiteIpKitInventory(studyId: string, siteId: string): Promise<any[]> {
    const res = await fetch(
      `/api/v1/execution/rtsm/kits/inventory/${studyId}/${siteId}`
    );
    if (!res.ok) {
      throw new Error("Failed to load site IP kit inventory");
    }
    return res.json();
  },

  /** Validates scanned barcode against site inventory (POST /api/v1/execution/rtsm/kits/validate-scan). */
  async validateIpKitScan(
    studyId: string,
    siteId: string,
    scannedBarcode: string
  ): Promise<any> {
    const res = await fetch("/api/v1/execution/rtsm/kits/validate-scan", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        study_id: studyId,
        site_id: siteId,
        scanned_barcode: scannedBarcode,
      }),
    });
    if (!res.ok) {
      throw new Error("Failed to validate barcode scan");
    }
    return res.json();
  },
};
