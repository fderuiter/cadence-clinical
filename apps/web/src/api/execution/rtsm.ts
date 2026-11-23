import { encodeChangeReason } from "../changeReason";
import { createOpenApiClient, unwrapResponse } from "../client";
import type { components, paths } from "../generated/execution";

type Schemas = components["schemas"];

export type Subject = Schemas["Execution_SubjectResponse"];
export type ComplianceStatus = Schemas["Execution_ComplianceStatusResponse"];
export type OperationEnvelope = Schemas["Execution_OperationEnvelopeResponse"];
export type RandomizationScheme =
  Schemas["Execution_RandomizationSchemeResponse"];
export type RandomizationSchemeDraftRequest =
  Schemas["Execution_RandomizationSchemeDraftRequest"];
export type RandomizeSubjectRequest =
  Schemas["Execution_RandomizeSubjectRequest"];
export type RandomizationResponse = Schemas["Execution_RandomizationResponse"];
export type ProtocolVersionPayload =
  Schemas["Execution_ProtocolVersionPayload"];
export type SubjectConsentRequest = Schemas["Execution_SubjectConsentRequest"];
export type SubjectConsentResponse =
  Schemas["Execution_SubjectConsentResponse"];
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

export interface AddSubjectPayload {
  studyId: string;
  subjectCode: string;
  siteId?: string;
  reason: string;
}

const client = createOpenApiClient<paths>();

/**
 * Route-only RTSM, IP supply accountability, CGT chain-of-custody, consent, and site-activation
 * endpoints for the Execution service. Loaded on demand by `executionApi` so cold-boot shell chunks
 * only ship participant directory reads.
 */
export const executionRtsmApi = {
  addSubject({
    studyId,
    subjectCode,
    siteId,
    reason,
  }: AddSubjectPayload): Promise<Subject> {
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

  assignSubjectSite(
    subjectId: string,
    siteId: string,
    reason: string
  ): Promise<Subject> {
    return unwrapResponse(
      client.PATCH("/api/v1/execution/subjects/{subjectId}/site", {
        params: { path: { subjectId } },
        body: { site_id: siteId },
        headers: { "X-Change-Reason": encodeChangeReason(reason) },
      })
    );
  },

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
   * documents complete and CTMS startup readiness issued a valid HMAC `greenlightToken`, auditing the
   * attempt either way with the reason sent as X-Change-Reason (encoded per ADR-2495).
   */
  activateSite(
    siteId: string,
    studyId: string,
    changeReason: string,
    greenlightToken?: string | null
  ): Promise<OperationEnvelope> {
    return unwrapResponse(
      client.POST("/api/v1/execution/sites/{siteId}/activate", {
        params: { path: { siteId } },
        body: {
          study_id: studyId,
          ...(greenlightToken !== undefined
            ? { greenlight_token: greenlightToken }
            : {}),
        },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

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

  getIpKitCertificate(
    certificateId: string
  ): Promise<IpKitReconciliationCertificate> {
    return unwrapResponse(
      client.GET("/api/v1/execution/rtsm/kits/certificates/{certificateId}", {
        params: { path: { certificateId } },
      })
    ) as unknown as Promise<IpKitReconciliationCertificate>;
  },

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

  listRandomizationSchemes(studyId: string): Promise<RandomizationScheme[]> {
    return unwrapResponse(
      client.GET("/api/v1/execution/randomization/schemes", {
        params: { query: { study_id: studyId } },
      })
    );
  },

  draftRandomizationScheme(
    payload: RandomizationSchemeDraftRequest,
    changeReason: string
  ): Promise<RandomizationScheme> {
    return unwrapResponse(
      client.POST("/api/v1/execution/randomization/schemes", {
        body: payload,
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  approveRandomizationScheme(
    schemeId: string,
    changeReason: string,
    sigToken: string
  ): Promise<RandomizationScheme> {
    return unwrapResponse(
      client.POST(
        "/api/v1/execution/randomization/schemes/{schemeId}/approve",
        {
          params: { path: { schemeId } },
          headers: {
            "X-Change-Reason": encodeChangeReason(changeReason),
            "X-Sig-Token": sigToken,
          },
        }
      )
    );
  },

  recordSubjectConsent(
    subjectId: string,
    payload: SubjectConsentRequest,
    changeReason?: string
  ): Promise<SubjectConsentResponse> {
    return unwrapResponse(
      client.POST("/api/v1/execution/subjects/{subjectId}/consent", {
        params: { path: { subjectId } },
        body: payload,
        headers: changeReason
          ? { "X-Change-Reason": encodeChangeReason(changeReason) }
          : undefined,
      })
    );
  },

  randomizeSubject(
    subjectId: string,
    payload: RandomizeSubjectRequest,
    changeReason: string,
    sigToken: string
  ): Promise<RandomizationResponse> {
    return unwrapResponse(
      client.POST("/api/v1/execution/subjects/{subjectId}/randomize", {
        params: { path: { subjectId } },
        body: payload,
        headers: {
          "X-Change-Reason": encodeChangeReason(changeReason),
          "X-Sig-Token": sigToken,
        },
      })
    );
  },

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

  listCgtCertificates(
    studyId: string
  ): Promise<Array<Record<string, unknown>>> {
    return unwrapResponse(
      client.GET("/api/v1/execution/rtsm/cgt/certificates/{studyId}", {
        params: { path: { studyId } },
      })
    ) as unknown as Promise<Array<Record<string, unknown>>>;
  },

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
