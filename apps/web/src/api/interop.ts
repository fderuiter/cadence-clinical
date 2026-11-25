import { createOpenApiClient, unwrapResponse } from "./client";
import type {
  components as InteropComponents,
  paths as InteropPaths,
} from "./generated/interop";
import { openApiClient } from "./legacyClient";
import type { paths } from "./types";

type InteropSchemas = InteropComponents["schemas"];

const interopClient = createOpenApiClient<InteropPaths>();

export interface CandidateListParams {
  studyId?: string;
  study_id?: string;
  subjectId?: string;
  subject_id?: string;
  siteId?: string;
  site_id?: string;
  domain?: string;
  status?: string;
  skip?: number;
  limit?: number;
}

export interface NormalizedEhrCandidate {
  id: string;
  study_id: string;
  site_id: string;
  subject_id: string;
  subject_pseudonym: string;
  ehr_system_id: string;
  fhir_resource_type: string;
  fhir_resource_id: string;
  source_timestamp: string;
  target_domain: string;
  target_variable: string;
  cdash_testcd?: string | null;
  cdash_test?: string | null;
  candidate_value: unknown;
  candidate_unit?: string | null;
  current_ecrf_value?: unknown;
  current_ecrf_updated_at?: string | null;
  discrepancy_type?: string;
  confidence_score?: number;
  mapping_provenance: string;
  raw_fhir_resource?: {
    [key: string]: unknown;
  };
  status?: string;
  resolution_action?: string | null;
  resolution_reason?: string | null;
  resolved_by?: string | null;
  resolved_at?: string | null;
  electronic_signature?: {
    [key: string]: unknown;
  } | null;
  clinical_query_id?: string | null;
  created_at?: string | null;
  version_index?: number;
  [key: string]: unknown;
}

export interface NormalizedEhrCandidateListResponse {
  candidates: NormalizedEhrCandidate[];
  total_count: number;
  totalCount?: number;
}

export interface NormalizedEhrReconcileActionResponse {
  success: boolean;
  candidate: NormalizedEhrCandidate;
  clinical_query_id?: string | null;
  clinicalQueryId?: string | null;
  message: string;
}

export interface NormalizedEhrReconcileBatchActionResponse {
  success: boolean;
  action_count: number;
  actionCount?: number;
  candidates: NormalizedEhrCandidate[];
  message: string;
}

function toRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}

function pickString(
  record: Record<string, unknown>,
  camelKey: string,
  snakeKey: string,
  fallback = ""
): string {
  const value = record[camelKey] ?? record[snakeKey];
  return typeof value === "string" ? value : fallback;
}

function pickNullableString(
  record: Record<string, unknown>,
  camelKey: string,
  snakeKey: string
): string | null {
  const value = record[camelKey] ?? record[snakeKey];
  if (value === undefined || value === null) return null;
  return String(value);
}

function pickStringArray(
  record: Record<string, unknown>,
  camelKey: string,
  snakeKey: string
): string[] {
  const value = record[camelKey] ?? record[snakeKey];
  return Array.isArray(value) ? value.map((item) => String(item)) : [];
}

function normalizeNumeric(value: unknown, fallback: number): number {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string" && value.trim() !== "") {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) return parsed;
  }
  return fallback;
}

function normalizeEhrCandidate(raw: unknown): NormalizedEhrCandidate {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) {
    return raw as NormalizedEhrCandidate;
  }
  const d = raw as Record<string, unknown>;
  const currentUpdatedAt =
    (d.current_ecrf_updated_at as string | null | undefined) ??
    (d.currentEcrfUpdatedAt as string | null | undefined) ??
    null;
  const rawCurrentEcrfValue =
    d.current_ecrf_value !== undefined
      ? d.current_ecrf_value
      : d.currentEcrfValue === "" && currentUpdatedAt === null
        ? null
        : (d.currentEcrfValue ?? null);
  const rawFhir =
    d.raw_fhir_resource !== undefined ? d.raw_fhir_resource : d.rawFhirResource;
  const normalizedRawFhir =
    rawFhir && typeof rawFhir === "object" && !Array.isArray(rawFhir)
      ? (rawFhir as Record<string, unknown>)
      : {};
  const rawSig =
    d.electronic_signature !== undefined
      ? d.electronic_signature
      : d.electronicSignature;
  const normalizedSig =
    rawSig && typeof rawSig === "object" && !Array.isArray(rawSig)
      ? (rawSig as Record<string, unknown>)
      : null;

  return {
    ...d,
    id: String(d.id ?? ""),
    study_id: String(d.study_id ?? d.studyId ?? ""),
    site_id: String(d.site_id ?? d.siteId ?? ""),
    subject_id: String(d.subject_id ?? d.subjectId ?? ""),
    subject_pseudonym: String(d.subject_pseudonym ?? d.subjectPseudonym ?? ""),
    ehr_system_id: String(d.ehr_system_id ?? d.ehrSystemId ?? ""),
    fhir_resource_type: String(
      d.fhir_resource_type ?? d.fhirResourceType ?? ""
    ),
    fhir_resource_id: String(d.fhir_resource_id ?? d.fhirResourceId ?? ""),
    source_timestamp: String(d.source_timestamp ?? d.sourceTimestamp ?? ""),
    target_domain: String(d.target_domain ?? d.targetDomain ?? ""),
    target_variable: String(d.target_variable ?? d.targetVariable ?? ""),
    cdash_testcd:
      (d.cdash_testcd as string | null | undefined) ??
      (d.cdashTestcd as string | null | undefined) ??
      null,
    cdash_test:
      (d.cdash_test as string | null | undefined) ??
      (d.cdashTest as string | null | undefined) ??
      null,
    candidate_value:
      d.candidate_value !== undefined ? d.candidate_value : d.candidateValue,
    candidate_unit:
      (d.candidate_unit as string | null | undefined) ??
      (d.candidateUnit as string | null | undefined) ??
      null,
    current_ecrf_value: rawCurrentEcrfValue,
    current_ecrf_updated_at: currentUpdatedAt,
    discrepancy_type: String(
      d.discrepancy_type ?? d.discrepancyType ?? "NEW_FIELD"
    ),
    confidence_score: normalizeNumeric(
      d.confidence_score ?? d.confidenceScore,
      1
    ),
    mapping_provenance: String(
      d.mapping_provenance ?? d.mappingProvenance ?? ""
    ),
    raw_fhir_resource: normalizedRawFhir,
    status: String(d.status ?? "PENDING_REVIEW"),
    resolution_action:
      (d.resolution_action as string | null | undefined) ??
      (d.resolutionAction as string | null | undefined) ??
      null,
    resolution_reason:
      (d.resolution_reason as string | null | undefined) ??
      (d.resolutionReason as string | null | undefined) ??
      null,
    resolved_by:
      (d.resolved_by as string | null | undefined) ??
      (d.resolvedBy as string | null | undefined) ??
      null,
    resolved_at:
      (d.resolved_at as string | null | undefined) ??
      (d.resolvedAt as string | null | undefined) ??
      null,
    electronic_signature: normalizedSig,
    clinical_query_id:
      (d.clinical_query_id as string | null | undefined) ??
      (d.clinicalQueryId as string | null | undefined) ??
      null,
    created_at:
      (d.created_at as string | null | undefined) ??
      (d.createdAt as string | null | undefined) ??
      null,
    version_index: normalizeNumeric(d.version_index ?? d.versionIndex, 1),
  };
}

function normalizeEhrCandidateListResponse(
  raw: unknown
): NormalizedEhrCandidateListResponse {
  const d = toRecord(raw);
  const rawCandidates = Array.isArray(d.candidates) ? d.candidates : [];
  const candidates = rawCandidates.map((item) => normalizeEhrCandidate(item));
  const totalCount = normalizeNumeric(
    d.total_count ?? d.totalCount,
    candidates.length
  );
  return {
    ...d,
    candidates,
    total_count: totalCount,
    totalCount,
  };
}

function normalizeEhrActionResponse(
  raw: unknown
): NormalizedEhrReconcileActionResponse {
  const d = toRecord(raw);
  const clinicalQueryId =
    (d.clinical_query_id as string | null | undefined) ??
    (d.clinicalQueryId as string | null | undefined) ??
    null;
  return {
    ...d,
    success: Boolean(d.success),
    candidate: normalizeEhrCandidate(d.candidate),
    clinical_query_id: clinicalQueryId,
    clinicalQueryId,
    message: typeof d.message === "string" ? d.message : "",
  };
}

function normalizeEhrBatchActionResponse(
  raw: unknown
): NormalizedEhrReconcileBatchActionResponse {
  const d = toRecord(raw);
  const rawCandidates = Array.isArray(d.candidates) ? d.candidates : [];
  const candidates = rawCandidates.map((item) => normalizeEhrCandidate(item));
  const actionCount = normalizeNumeric(
    d.action_count ?? d.actionCount,
    candidates.length
  );
  return {
    ...d,
    success: Boolean(d.success),
    action_count: actionCount,
    actionCount,
    candidates,
    message: typeof d.message === "string" ? d.message : "",
  };
}

export interface TelehealthSessionParams {
  studyId?: string;
  subjectId?: string;
}

export interface PediatricAssentParams {
  studyId?: string;
  subjectId?: string;
}

export interface DtpShipmentParams {
  studyId?: string;
  siteId?: string;
  subjectPseudonymId?: string;
}

export interface InHomeVisitParams {
  studyId?: string;
  siteId?: string;
  nurseId?: string;
  subjectId?: string;
}

export const interopService = {
  registerSensorDevice(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sensors/devices", {
        body: body as paths["/api/v1/interop/sensors/devices"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listSensorDevices(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sensors/devices", {
        params: { query: { study_id: studyId } },
        ...options,
      })
    );
  },

  ingestSensorTelemetry(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sensors/ingest", {
        body: body as paths["/api/v1/interop/sensors/ingest"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listDailySummaries(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sensors/daily-summaries", {
        params: { query: { study_id: studyId } },
        ...options,
      })
    );
  },

  listSafetyAlerts(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sensors/safety-alerts", {
        params: { query: { study_id: studyId } },
        ...options,
      })
    );
  },

  acknowledgeSafetyAlert(
    alertId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge",
        {
          params: { path: { alert_id: alertId } },
          body: body as paths["/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  getSensorMetrics(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sensors/metrics/{study_id}", {
        params: { path: { study_id: studyId } },
        ...options,
      })
    );
  },

  async listEhrCandidates(
    params: CandidateListParams = {},
    options: Record<string, unknown> = {}
  ): Promise<NormalizedEhrCandidateListResponse> {
    const raw = await unwrapResponse(
      interopClient.GET("/api/v1/interop/esource/candidates", {
        params: {
          query: {
            study_id: params.studyId ?? params.study_id,
            subject_id: params.subjectId ?? params.subject_id,
            site_id: params.siteId ?? params.site_id,
            domain: params.domain,
            status: params.status,
            skip: params.skip ?? 0,
            limit: params.limit ?? 50,
          },
        },
        ...options,
      })
    );
    return normalizeEhrCandidateListResponse(raw);
  },

  async acceptEhrCandidate(
    body: unknown,
    options: Record<string, unknown> = {}
  ): Promise<NormalizedEhrReconcileActionResponse> {
    const input = toRecord(body);
    const requestBody: InteropSchemas["Interop_EhrReconcileAcceptRequest"] = {
      candidateId: pickString(input, "candidateId", "candidate_id"),
      commitReason: pickNullableString(input, "commitReason", "commit_reason"),
      changeReason: pickNullableString(input, "changeReason", "change_reason"),
    };
    const raw = await unwrapResponse(
      interopClient.POST("/api/v1/interop/esource/reconcile/accept", {
        body: requestBody,
        ...options,
      })
    );
    return normalizeEhrActionResponse(raw);
  },

  async batchAcceptEhrCandidates(
    body: unknown,
    options: Record<string, unknown> = {}
  ): Promise<NormalizedEhrReconcileBatchActionResponse> {
    const input = toRecord(body);
    const requestBody: InteropSchemas["Interop_EhrReconcileBatchAcceptRequest"] =
      {
        candidateIds: pickStringArray(input, "candidateIds", "candidate_ids"),
        commitReason: pickNullableString(
          input,
          "commitReason",
          "commit_reason"
        ),
        changeReason: pickNullableString(
          input,
          "changeReason",
          "change_reason"
        ),
      };
    const raw = await unwrapResponse(
      interopClient.POST("/api/v1/interop/esource/reconcile/batch-accept", {
        body: requestBody,
        ...options,
      })
    );
    return normalizeEhrBatchActionResponse(raw);
  },

  async rejectEhrCandidate(
    body: unknown,
    options: Record<string, unknown> = {}
  ): Promise<NormalizedEhrReconcileActionResponse> {
    const input = toRecord(body);
    const requestBody: InteropSchemas["Interop_EhrReconcileRejectRequest"] = {
      candidateId: pickString(input, "candidateId", "candidate_id"),
      rejectionReason: pickString(input, "rejectionReason", "rejection_reason"),
      changeReason: pickNullableString(input, "changeReason", "change_reason"),
    };
    const raw = await unwrapResponse(
      interopClient.POST("/api/v1/interop/esource/reconcile/reject", {
        body: requestBody,
        ...options,
      })
    );
    return normalizeEhrActionResponse(raw);
  },

  async batchRejectEhrCandidates(
    body: unknown,
    options: Record<string, unknown> = {}
  ): Promise<NormalizedEhrReconcileBatchActionResponse> {
    const input = toRecord(body);
    const requestBody: InteropSchemas["Interop_EhrReconcileBatchRejectRequest"] =
      {
        candidateIds: pickStringArray(input, "candidateIds", "candidate_ids"),
        rejectionReason: pickString(
          input,
          "rejectionReason",
          "rejection_reason"
        ),
        changeReason: pickNullableString(
          input,
          "changeReason",
          "change_reason"
        ),
      };
    const raw = await unwrapResponse(
      interopClient.POST("/api/v1/interop/esource/reconcile/batch-reject", {
        body: requestBody,
        ...options,
      })
    );
    return normalizeEhrBatchActionResponse(raw);
  },

  async raiseEhrClinicalQuery(
    body: unknown,
    options: Record<string, unknown> = {}
  ): Promise<NormalizedEhrReconcileActionResponse> {
    const input = toRecord(body);
    const requestBody: InteropSchemas["Interop_EhrReconcileQueryRequest"] = {
      candidateId: pickString(input, "candidateId", "candidate_id"),
      queryExplanation: pickString(
        input,
        "queryExplanation",
        "query_explanation"
      ),
      discrepancyType: pickNullableString(
        input,
        "discrepancyType",
        "discrepancy_type"
      ),
      changeReason: pickNullableString(input, "changeReason", "change_reason"),
    };
    const raw = await unwrapResponse(
      interopClient.POST("/api/v1/interop/esource/reconcile/query", {
        body: requestBody,
        ...options,
      })
    );
    return normalizeEhrActionResponse(raw);
  },

  // Telehealth & DCT Methods
  scheduleTelehealthSession(
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/dct/telehealth/sessions", {
        body: body as paths["/api/v1/interop/dct/telehealth/sessions"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listTelehealthSessions(
    params: TelehealthSessionParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/telehealth/sessions", {
        params: {
          query: {
            study_id: params.studyId,
            subject_id: params.subjectId,
          },
        },
        ...options,
      })
    );
  },

  listPediatricAssents(
    params: PediatricAssentParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/econsent/pediatric-assent", {
        params: {
          query: {
            study_id: params.studyId,
            subject_id: params.subjectId,
          },
        },
        ...options,
      })
    );
  },

  createDtpShipment(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/dct/dtp/shipments", {
        body: body as paths["/api/v1/interop/dct/dtp/shipments"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listDtpShipments(
    params: DtpShipmentParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/dtp/shipments", {
        params: {
          query: {
            study_id: params.studyId,
            site_id: params.siteId,
            subject_pseudonym_id: params.subjectPseudonymId,
          },
        },
        ...options,
      })
    );
  },

  listInHomeVisits(
    params: InHomeVisitParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/home-health/visits", {
        params: {
          query: {
            study_id: params.studyId,
            site_id: params.siteId,
            nurse_id: params.nurseId,
            subject_id: params.subjectId,
          },
        },
        ...options,
      })
    );
  },

  getDctMetrics(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/metrics/{study_id}", {
        params: { path: { study_id: studyId } },
        ...options,
      })
    );
  },
};
