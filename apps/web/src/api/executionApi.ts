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
export type RandomizationScheme =
  Schemas["Execution_RandomizationSchemeResponse"];
export type IpKitBatchReconciliationRequest =
  Schemas["Execution_IpKitBatchReconciliationRequest"];
export type IpKitItemReconcileRequest =
  Schemas["Execution_IpKitItemReconcileRequest"];
export type IpKitScanValidateRequest =
  Schemas["Execution_IpKitScanValidateRequest"];
export type IpKitSeedItem = Schemas["Execution_IpKitSeedItem"];
export type SeedInventoryRequest = Schemas["Execution_SeedInventoryRequest"];
export type CgtApheresisCreateRequest =
  Schemas["Execution_CgtApheresisCreateRequest"];
export type CgtMilestoneCreateRequest =
  Schemas["Execution_CgtMilestoneCreateRequest"];

export interface SiteIpKitInventoryItem {
  id?: string;
  study_id: string;
  site_id: string;
  kit_number: string;
  lot_number: string;
  status: string;
  expiration_date?: string | null;
  residual_volume_ml?: number | null;
  residual_volume_percentage?: number | null;
  treatment_arm?: string | null;
}

export interface IpKitScanValidationResult {
  kit_number: string;
  study_id: string;
  site_id: string;
  found_in_inventory: boolean;
  expected_lot_number?: string | null;
  scanned_lot_number?: string | null;
  lot_mismatch_detected: boolean;
  current_status?: string | null;
  recommended_disposition?: string | null;
  warning?: string | null;
}

export interface IpKitReconciliationCertificate {
  certificate_id: string;
  study_id: string;
  site_id: string;
  processed_by?: string | null;
  processed_at?: string | null;
  total_kits_reconciled?: number;
  quarantined_count?: number;
  returned_to_site_count?: number;
  returned_to_depot_count?: number;
  lot_mismatch_count?: number;
  notes?: string | null;
  payload_hash?: string | null;
  items?: Array<Record<string, unknown>>;
}

export interface CgtChainOfCustodyTrace {
  chain_of_custody_id?: string;
  id?: string;
  study_id: string;
  subject_id: string;
  din?: string | null;
  batch_id?: string | null;
  collection_site_id?: string | null;
  current_stage?: string | null;
  status?: string | null;
  milestones?: Array<{
    id?: string;
    stage: string;
    milestone_type: string;
    timestamp?: string | null;
    location_name?: string | null;
    temperature_celsius?: number | null;
    seal_intact?: boolean;
    custodian_name?: string | null;
    tracking_number?: string | null;
    notes?: string | null;
  }>;
}

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

  /** Full detail for a participant by surrogate UUID or subject code (GET /api/v1/execution/subjects/{subjectId}). */
  getSubjectDetail(subjectId: string): Promise<SubjectDetail> {
    return unwrapResponse(
      client.GET("/api/v1/execution/subjects/{subjectId}", {
        params: {
          path: { subjectId },
        },
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
  reconcileIpKitBatch(
    payload: IpKitBatchReconciliationRequest
  ): Promise<{ certificate?: IpKitReconciliationCertificate }> {
    return unwrapResponse(
      client.POST("/api/v1/execution/rtsm/kits/reconcile-batch", {
        body: payload,
        headers: {
          "X-Change-Reason": encodeChangeReason(payload.reason_for_change),
        },
      })
    ) as unknown as Promise<{ certificate?: IpKitReconciliationCertificate }>;
  },

  /** Retrieves site IP kit inventory catalog (GET /api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}). */
  getSiteIpKitInventory(
    studyId: string,
    siteId: string
  ): Promise<SiteIpKitInventoryItem[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/rtsm/kits/inventory/{studyId}/{siteId}", {
        params: { path: { studyId, siteId } },
      })
    ) as unknown as Promise<SiteIpKitInventoryItem[]>;
  },

  /** Seeds site IP kit inventory (POST /api/v1/execution/rtsm/kits/seed-inventory). */
  seedSiteIpKitInventory(
    payload: SeedInventoryRequest,
    changeReason: string
  ): Promise<unknown> {
    return unwrapResponse(
      client.POST("/api/v1/execution/rtsm/kits/seed-inventory", {
        body: payload,
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Retrieves a Part 11 IP kit reconciliation certificate (GET /api/v1/execution/rtsm/kits/certificates/{certificateId}). */
  getIpKitCertificate(
    certificateId: string
  ): Promise<IpKitReconciliationCertificate> {
    return unwrapResponse(
      client.GET("/api/v1/execution/rtsm/kits/certificates/{certificateId}", {
        params: { path: { certificateId } },
      })
    ) as unknown as Promise<IpKitReconciliationCertificate>;
  },

  /** Validates scanned barcode against site inventory (POST /api/v1/execution/rtsm/kits/validate-scan). */
  validateIpKitScan(
    studyId: string,
    siteId: string,
    scannedBarcode: string
  ): Promise<IpKitScanValidationResult> {
    const [kitPart, lotPart] = scannedBarcode.split("|");
    return unwrapResponse(
      client.POST("/api/v1/execution/rtsm/kits/validate-scan", {
        body: {
          study_id: studyId,
          site_id: siteId,
          kit_number: (kitPart ?? scannedBarcode).trim(),
          scanned_lot_number: lotPart ? lotPart.trim() : null,
        },
      })
    ) as unknown as Promise<IpKitScanValidationResult>;
  },

  /** Lists a study's randomization scheme versions without blinded allocation lists (GET /api/v1/execution/randomization/schemes). */
  listRandomizationSchemes(studyId: string): Promise<RandomizationScheme[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/randomization/schemes", {
        params: { query: { study_id: studyId } },
      })
    );
  },

  /** Retrieves autologous Cell & Gene Therapy chain-of-custody trace for a participant. */
  getChainOfCustody(
    studyId: string,
    subjectId: string
  ): Promise<CgtChainOfCustodyTrace> {
    return unwrapResponse(
      client.GET(
        "/api/v1/execution/rtsm/cgt/chain-of-custody/{studyId}/{subjectId}",
        {
          params: { path: { studyId, subjectId } },
        }
      )
    ) as unknown as Promise<CgtChainOfCustodyTrace>;
  },

  /** Initiates an autologous Cell & Gene Therapy apheresis chain-of-custody record. */
  recordCgtApheresis(
    payload: CgtApheresisCreateRequest,
    changeReason: string
  ): Promise<unknown> {
    return unwrapResponse(
      client.POST("/api/v1/execution/rtsm/cgt/apheresis", {
        body: payload,
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Appends a cryogenic transit or facility custody milestone to an active chain-of-custody record. */
  recordCgtMilestone(
    payload: CgtMilestoneCreateRequest,
    changeReason: string
  ): Promise<unknown> {
    return unwrapResponse(
      client.POST("/api/v1/execution/rtsm/cgt/milestone", {
        body: payload,
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Lists study CGT destruction certificates. */
  listCgtCertificates(
    studyId: string
  ): Promise<Array<Record<string, unknown>>> {
    return unwrapResponse(
      client.GET("/api/v1/execution/rtsm/cgt/certificates/{studyId}", {
        params: { path: { studyId } },
      })
    ) as unknown as Promise<Array<Record<string, unknown>>>;
  },

  /** Lists study CGT vial return reconciliations. */
  listCgtReconciliations(
    studyId: string
  ): Promise<Array<Record<string, unknown>>> {
    return unwrapResponse(
      client.GET("/api/v1/execution/rtsm/cgt/reconciliations/{studyId}", {
        params: { path: { studyId } },
      })
    ) as unknown as Promise<Array<Record<string, unknown>>>;
  },
};
