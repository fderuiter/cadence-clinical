import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/execution";

/**
 * Typed client for the .NET Execution service, generated from docs/openapi/execution_openapi.json.
 *
 * Every Execution call in the web app goes through this client (docs/web/API_MIGRATION.md). eCRF forms are not here:
 * they are kernel form instances served by the kernel host (ecrfFormsApi.ts, ADR-2622), and so are their queries
 * (ecrfQueriesApi.ts, #5461).
 */
type Schemas = components["schemas"];

export type SubjectSummary = Schemas["Execution_SubjectSummaryResponse"];
export type SubjectList = Schemas["Execution_SubjectListResponse"];
export type Subject = Schemas["Execution_SubjectResponse"];
export type SubjectDetail = Schemas["Execution_SubjectDetailResponse"];
export type ComplianceStatus = Schemas["Execution_ComplianceStatusResponse"];
export type OperationEnvelope = Schemas["Execution_OperationEnvelopeResponse"];

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
  q?: string;
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
            q: query.q,
            limit: query.limit,
            offset: query.offset,
          },
        },
      })
    );
  },

  /** One participant's detail by surrogate UUID or subject code (GET /api/v1/execution/subjects/{subjectId}). */
  getSubject(subjectId: string): Promise<SubjectDetail> {
    return unwrapResponse(
      client.GET("/api/v1/execution/subjects/{subjectId}", {
        params: { path: { subjectId } },
      })
    );
  },

  /**
   * Adds a participant to a study in screening (POST /api/v1/execution/subjects). The canvas sends no
   * demographics: identity details are not captured here. A participant is never given an arm at creation;
   * randomisation has its own route (ADR-2640). The reason reaches
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
