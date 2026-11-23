import { buildStepUpVerificationPayload, SEMANTIC_ACTIONS } from "ui";
import { encodeChangeReason } from "./changeReason";
import {
  ApiError,
  createOpenApiClient,
  openApiClient,
  unwrapResponse,
} from "./client";
import type { components, paths as EtmfPaths } from "./generated/etmf";

type EtmfSchemas = components["schemas"];

export interface EtmfDocumentParams {
  study_id?: string;
  site_id?: string | null;
  zone?: string;
  search?: string;
}

export interface StageExistingDocumentQcPayload {
  reason_for_change?: string | null;
  reasonForChange?: string | null;
  assigned_cra?: string | null;
  assignedCra?: string | null;
}

export interface EtmfCompletenessParams {
  study_id?: string;
  milestone?: string;
  site_id?: string;
}

export interface SignatureVerificationPayload {
  username: string;
  password?: string;
  totp?: string | null;
  action: string;
  semanticAction?: string;
}

export interface DocumentSignOffPayload {
  signingReason: string;
}

export interface SignOptions {
  changeReason?: string;
  sigToken?: string;
}

export type ExportDocumentsPayload = EtmfSchemas["Etmf_ExportDocumentsRequest"];

export interface ExportJobStatus {
  job_id: string;
  study_id: string;
  requested_by: string;
  status: "QUEUED" | "PROCESSING" | "COMPLETED" | "FAILED";
  progress_percentage: number;
  total_documents: number;
  processed_documents: number;
  download_url?: string;
  expires_at?: string;
  created_at: string;
}

export interface StageQcDocumentPayload {
  content?: string | null;
  content_base64?: string | null;
  contentBase64?: string | null;
  filename: string;
  study_id?: string;
  studyId?: string;
  site_id?: string | null;
  siteId?: string | null;
  mime_type?: string | null;
  mimeType?: string | null;
  artifact_hint?: string | null;
  artifactHint?: string | null;
  taxonomy_version?: string | null;
  taxonomyVersion?: string | null;
  reason_for_change?: string;
  reasonForChange?: string;
  assigned_cra?: string | null;
  assignedCra?: string | null;
}

export interface SubmitDocumentForQcPayload {
  reason_for_change?: string;
  reasonForChange?: string;
  assigned_cra?: string | null;
  assignedCra?: string | null;
}

export interface UpdateDocumentTaxonomyPayload {
  zone?: number | string | null;
  section?: string | null;
  artifact_code?: string | null;
  artifactCode?: string | null;
  artifact_type?: string | null;
  artifactType?: string | null;
  reason_for_change?: string | null;
  reasonForChange?: string | null;
}

export interface SubmitQcReviewPayload {
  decision: string;
  reason_for_change?: string;
  reasonForChange?: string;
  override_artifact_code?: string | null;
  overrideArtifactCode?: string | null;
  discrepancy_comment?: string | null;
  discrepancyComment?: string | null;
}

const etmfClient = createOpenApiClient<EtmfPaths>();

async function throwStreamApiError(
  response: Response,
  prefix: string
): Promise<never> {
  const errorText = await response.text();
  let parsedData: unknown = errorText;
  let detailMessage = errorText;
  if (errorText) {
    try {
      const parsed = JSON.parse(errorText) as Record<string, unknown>;
      parsedData = parsed;
      const extracted = parsed.detail ?? parsed.error ?? parsed.title;
      if (typeof extracted === "string" && extracted.trim()) {
        detailMessage = extracted.trim();
      }
    } catch {
      // Keep raw errorText when body is plain text
    }
  }
  throw new ApiError(
    detailMessage
      ? `${prefix} (${response.status}): ${detailMessage}`
      : `${prefix} (${response.status})`,
    response.status,
    response.statusText,
    parsedData
  );
}

function parseContentDispositionFilename(
  headerValue: string | null,
  fallback: string
): string {
  if (!headerValue) return fallback;
  const utf8Match = headerValue.match(/filename\*=UTF-8''([^;]+)/i);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1]);
    } catch {
      return utf8Match[1];
    }
  }
  const match = headerValue.match(/filename="?([^";]+)"?/);
  return match?.[1] ? match[1] : fallback;
}

function normalizeClassificationCandidate(raw: Record<string, unknown> | null) {
  if (!raw || typeof raw !== "object") return raw;
  return {
    ...raw,
    zone: raw.zone,
    section: raw.section,
    artifact_code: (raw.artifact_code ?? raw.artifactCode ?? "") as string,
    artifact_type: (raw.artifact_type ?? raw.artifactType ?? "") as string,
    confidence: raw.confidence,
  };
}

function normalizeIntelligenceReport(raw: unknown) {
  if (!raw || typeof raw !== "object") return raw;
  const r = raw as Record<string, unknown>;
  const primary = (r.primary_classification ?? r.primaryClassification) as
    Record<string, unknown> | undefined;
  const alternatives = (r.alternative_candidates ?? r.alternativeCandidates) as
    Array<Record<string, unknown>> | undefined;
  return {
    ...r,
    document_id: r.document_id ?? r.documentId ?? null,
    mime_type: r.mime_type ?? r.mimeType ?? "application/pdf",
    sha256_hash: r.sha256_hash ?? r.sha256Hash ?? "",
    confidence_tier: r.confidence_tier ?? r.confidenceTier ?? "MEDIUM",
    qc_recommendation: r.qc_recommendation ?? r.qcRecommendation ?? "",
    primary_classification: primary
      ? normalizeClassificationCandidate(primary)
      : undefined,
    alternative_candidates: Array.isArray(alternatives)
      ? alternatives.map((c) => normalizeClassificationCandidate(c))
      : [],
  };
}

function normalizeTmfDocument<T>(raw: T): T {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return raw;
  const d = raw as Record<string, unknown>;
  if (
    !("studyId" in d) &&
    !("artifactCode" in d) &&
    !("createdAt" in d) &&
    !("contentChecksum" in d) &&
    !("objectKey" in d)
  ) {
    return raw;
  }
  let parsedMeta = d.metadata_json ?? d.metadataJson ?? null;
  if (typeof parsedMeta === "string") {
    try {
      parsedMeta = JSON.parse(parsedMeta);
    } catch {
      // leave string if non-JSON
    }
  }
  if (
    parsedMeta &&
    typeof parsedMeta === "object" &&
    "document_intelligence" in (parsedMeta as Record<string, unknown>)
  ) {
    const metaObj = parsedMeta as Record<string, unknown>;
    parsedMeta = {
      ...metaObj,
      document_intelligence: normalizeIntelligenceReport(
        metaObj.document_intelligence
      ),
    };
  }
  return {
    ...d,
    id: d.id,
    study_id: d.study_id ?? d.studyId ?? "",
    site_id: d.site_id ?? d.siteId ?? null,
    zone: d.zone,
    section: d.section,
    artifact_code: d.artifact_code ?? d.artifactCode ?? "",
    artifact_type: d.artifact_type ?? d.artifactType ?? "",
    filename: d.filename,
    mime_type: d.mime_type ?? d.mimeType ?? "application/pdf",
    object_key: d.object_key ?? d.objectKey ?? null,
    content_checksum: d.content_checksum ?? d.contentChecksum ?? null,
    created_at: d.created_at ?? d.createdAt ?? "",
    created_by: d.created_by ?? d.createdBy ?? "",
    version_index: d.version_index ?? d.versionIndex ?? 1,
    status: d.status,
    approval_status: d.approval_status ?? d.status,
    reason_for_change: d.reason_for_change ?? d.reasonForChange ?? "",
    signer: d.signer ?? null,
    signing_timestamp: d.signing_timestamp ?? d.signingTimestamp ?? null,
    signature_manifestation:
      d.signature_manifestation ?? d.signatureManifestation ?? null,
    metadata_json: parsedMeta,
  } as unknown as T;
}

function normalizeQcStagingItem<T>(raw: T): T {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return raw;
  const item = raw as Record<string, unknown>;
  return {
    ...item,
    document_id: item.document_id ?? item.documentId ?? "",
    study_id: item.study_id ?? item.studyId ?? "",
    site_id: item.site_id ?? item.siteId ?? null,
    filename: item.filename ?? "",
    status: item.status ?? "TECHNICAL_QC",
    staged_at: item.staged_at ?? item.stagedAt ?? "",
    staged_by: item.staged_by ?? item.stagedBy ?? "",
    qc_assigned_to: item.qc_assigned_to ?? item.qcAssignedTo ?? null,
    discrepancy_notes: item.discrepancy_notes ?? item.discrepancyNotes ?? [],
    intelligence_report: normalizeIntelligenceReport(
      item.intelligence_report ?? item.intelligenceReport
    ),
  } as unknown as T;
}

export const etmfService = {
  async exportDocuments(payload: Partial<ExportDocumentsPayload>) {
    const body: ExportDocumentsPayload = {
      study_id: payload.study_id ?? null,
      document_ids: payload.document_ids ?? null,
      zone: payload.zone ?? null,
      section: payload.section ?? null,
      artifact_code: payload.artifact_code ?? null,
      is_full_binder: payload.is_full_binder ?? false,
    };
    const { response } = await etmfClient.POST(
      "/api/v1/etmf/documents/export",
      {
        body,
        parseAs: "stream",
      }
    );

    if (response.status === 200) {
      const blob = await response.blob();
      const filename = parseContentDispositionFilename(
        response.headers.get("content-disposition"),
        `etmf_export_${payload.study_id || "study"}.zip`
      );
      return { mode: "stream" as const, blob, filename };
    }
    if (response.status === 202) {
      const jobData = await response.json();
      return { mode: "job" as const, job: jobData as ExportJobStatus };
    }
    return throwStreamApiError(response, "Export failed");
  },

  async getExportJob(jobId: string): Promise<ExportJobStatus> {
    const data = await unwrapResponse(
      etmfClient.GET("/api/v1/etmf/export-jobs/{job_id}", {
        params: { path: { job_id: jobId } },
      })
    );
    return data as unknown as ExportJobStatus;
  },

  async listExportJobs(studyId?: string): Promise<ExportJobStatus[]> {
    const data = await unwrapResponse(
      etmfClient.GET("/api/v1/etmf/export-jobs", {
        params: studyId ? { query: { study_id: studyId } } : {},
      })
    );
    return data as unknown as ExportJobStatus[];
  },

  async downloadExportJob(
    jobId: string
  ): Promise<{ blob: Blob; filename: string }> {
    const { response } = await etmfClient.GET(
      "/api/v1/etmf/export-jobs/{job_id}/download",
      {
        params: { path: { job_id: jobId } },
        parseAs: "stream",
      }
    );
    if (response.status === 200) {
      const blob = await response.blob();
      const filename = parseContentDispositionFilename(
        response.headers.get("content-disposition"),
        `etmf_export_${jobId}.zip`
      );
      return { blob, filename };
    }
    return throwStreamApiError(response, "Export download failed");
  },

  async getDocuments(
    params: EtmfDocumentParams = {},
    options: Record<string, unknown> = {}
  ) {
    const query: { study_id?: string; site_id?: string; zone?: number } = {};
    if (params.study_id) {
      query.study_id = params.study_id;
    }
    if (params.site_id) {
      query.site_id = params.site_id;
    }
    if (params.zone) {
      const parsedZone = parseInt(params.zone, 10);
      if (!Number.isNaN(parsedZone)) {
        query.zone = parsedZone;
      }
    }
    const data: unknown = await unwrapResponse(
      etmfClient.GET("/api/v1/etmf/documents", {
        params: Object.keys(query).length > 0 ? { query } : {},
        ...options,
      })
    );
    return Array.isArray(data)
      ? data.map((doc: unknown) => normalizeTmfDocument(doc))
      : data;
  },

  async getDocument(documentId: string, options: Record<string, unknown> = {}) {
    const data = await unwrapResponse(
      etmfClient.GET("/api/v1/etmf/documents/{document_id}", {
        params: { path: { document_id: documentId } },
        ...options,
      })
    );
    return normalizeTmfDocument(data);
  },

  async downloadDocumentContent(
    documentId: string
  ): Promise<{ blob: Blob; mimeType: string; sha256: string }> {
    const { response } = await etmfClient.GET(
      "/api/v1/etmf/documents/{document_id}/content",
      {
        params: { path: { document_id: documentId } },
        parseAs: "stream",
      }
    );
    if (response.status === 200) {
      const mimeType =
        response.headers.get("content-type") || "application/pdf";
      const sha256 = (response.headers.get("x-content-sha256") || "").trim();
      const blob = await response.blob();
      return { blob, mimeType, sha256 };
    }
    return throwStreamApiError(response, "Document content retrieval failed");
  },

  async ingestDocument(body: unknown, options: Record<string, unknown> = {}) {
    const raw = (body ?? {}) as Record<string, unknown>;
    const wireBody: EtmfSchemas["Etmf_IngestDocumentRequest"] = {
      filename: String(raw.filename ?? raw.file_name ?? "document.pdf"),
      content: (raw.content as string | null) ?? null,
      contentBase64:
        ((raw.contentBase64 ?? raw.content_base64) as string | null) ?? null,
      studyId: (raw.studyId ?? raw.study_id ?? null) as string | null,
      siteId: (raw.siteId ?? raw.site_id ?? null) as string | null,
      mimeType:
        ((raw.mimeType ?? raw.mime_type) as string | null) ?? "application/pdf",
      zone: (raw.zone as number | string) ?? 1,
      section: (raw.section as string | null) ?? null,
      artifactCode: (raw.artifactCode ?? raw.artifact_code ?? null) as
        string | null,
      artifactType: (raw.artifactType ?? raw.artifact_type ?? null) as
        string | null,
      taxonomyVersion:
        ((raw.taxonomyVersion ?? raw.taxonomy_version) as string | null) ??
        "v3.2.0-complete",
      reasonForChange: (raw.reasonForChange ??
        raw.reason_for_change ??
        null) as string | null,
    };
    const reason = wireBody.reasonForChange;
    const headers: Record<string, string> = {};
    if (reason) {
      headers["X-Change-Reason"] = encodeChangeReason(reason);
    }
    const data = await unwrapResponse(
      etmfClient.POST("/api/v1/etmf/ingest", {
        body: wireBody,
        headers,
        ...options,
      })
    );
    return normalizeTmfDocument(data);
  },

  async submitDocumentForQc(
    documentId: string,
    body: SubmitDocumentForQcPayload = {},
    options: Record<string, unknown> = {}
  ) {
    const reason =
      body.reasonForChange ??
      body.reason_for_change ??
      (typeof options.changeReason === "string" ? options.changeReason : "");
    const wireBody: EtmfSchemas["Etmf_SubmitDocumentForQcRequest"] = {
      reasonForChange: reason || null,
      assignedCra: body.assignedCra ?? body.assigned_cra ?? null,
    };
    const headers: Record<string, string> = {};
    if (reason) {
      headers["X-Change-Reason"] = encodeChangeReason(reason);
    }
    const data = await unwrapResponse(
      etmfClient.POST("/api/v1/etmf/documents/{document_id}/submit-qc", {
        params: { path: { document_id: documentId } },
        body: wireBody,
        headers,
        ...options,
      })
    );
    return normalizeTmfDocument(data);
  },

  async tagDocument(
    documentId: string,
    payload: UpdateDocumentTaxonomyPayload | Record<string, unknown>,
    options: Record<string, unknown> = {}
  ) {
    const raw = (payload ?? {}) as Record<string, unknown>;
    const reason =
      ((raw.reasonForChange ?? raw.reason_for_change) as string | undefined) ??
      (typeof options.changeReason === "string"
        ? options.changeReason
        : undefined) ??
      null;
    const wireBody: EtmfSchemas["Etmf_UpdateDocumentTaxonomyRequest"] = {
      zone: (raw.zone as number | string | null | undefined) ?? null,
      section: (raw.section as string | null | undefined) ?? null,
      artifactCode:
        ((raw.artifactCode ?? raw.artifact_code) as
          string | null | undefined) ?? null,
      artifactType:
        ((raw.artifactType ?? raw.artifact_type) as
          string | null | undefined) ?? null,
      reasonForChange: reason,
    };
    const headers: Record<string, string> = {};
    if (reason) {
      headers["X-Change-Reason"] = encodeChangeReason(reason);
    }
    const data = await unwrapResponse(
      etmfClient.PATCH("/api/v1/etmf/documents/{document_id}/taxonomy", {
        params: { path: { document_id: documentId } },
        body: wireBody,
        headers,
        ...options,
      })
    );
    return normalizeTmfDocument(data);
  },

  getCompleteness(
    params: EtmfCompletenessParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/etmf/completeness", {
        params: {
          query: {
            study_id: params.study_id || "",
            milestone: params.milestone || "",
            site_id: params.site_id ?? null,
          },
        },
        ...options,
      })
    );
  },

  verifySignature(
    {
      username,
      password,
      totp = null,
      action,
      semanticAction,
    }: SignatureVerificationPayload,
    options: Record<string, unknown> = {}
  ) {
    let semantic = semanticAction;
    if (!semantic) {
      if (action.includes("capture-consent")) {
        semantic = SEMANTIC_ACTIONS.ECONSENT_CAPTURE;
      } else if (action.includes("doa/sign-off")) {
        semantic = SEMANTIC_ACTIONS.CTMS_DOA_SIGN_OFF;
      } else if (action.includes("sign-off")) {
        semantic = SEMANTIC_ACTIONS.ETMF_DOCUMENT_SIGN_OFF;
      } else if (action.includes("chair-adjudicate")) {
        semantic = SEMANTIC_ACTIONS.CEC_CHAIR_ADJUDICATE;
      } else if (action.includes("/forms/")) {
        semantic = SEMANTIC_ACTIONS.COLLECTION_SIGN;
      } else {
        semantic = "execution.signature.verify";
      }
    }

    const body = buildStepUpVerificationPayload({
      username,
      password: password || "",
      totp,
      action,
      semanticAction: semantic,
    });

    return unwrapResponse(
      (openApiClient as any).POST("/api/v1/auth/signature-verification", {
        body,
        ...options,
      })
    );
  },

  signDocument(
    documentId: string,
    { signingReason }: DocumentSignOffPayload,
    { changeReason, sigToken }: SignOptions = {}
  ) {
    const headers: Record<string, string> = {};
    if (sigToken) {
      headers["X-Sig-Token"] = sigToken;
    }
    if (changeReason) {
      headers["X-Change-Reason"] = encodeChangeReason(changeReason);
    }

    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/documents/{document_id}/sign-off", {
        params: { path: { document_id: documentId } },
        body: { signing_reason: signingReason as never },
        headers,
      })
    );
  },

  async classifyIntelligence(
    body: {
      filename: string;
      content?: string;
      mime_type?: string;
      mimeType?: string;
      content_hint?: string;
      contentHint?: string;
      taxonomy_version?: string;
      taxonomyVersion?: string;
    },
    options: Record<string, unknown> = {}
  ) {
    const wireBody: EtmfSchemas["Etmf_ClassifyRequest"] = {
      filename: body.filename,
      content: body.content ?? null,
      mimeType: body.mimeType ?? body.mime_type ?? "application/pdf",
      contentHint: body.contentHint ?? body.content_hint ?? null,
      taxonomyVersion:
        body.taxonomyVersion ?? body.taxonomy_version ?? "v3.2.0-complete",
    };
    const data = await unwrapResponse(
      etmfClient.POST("/api/v1/etmf/intelligence/classify", {
        body: wireBody,
        ...options,
      })
    );
    return normalizeIntelligenceReport(data);
  },

  async stageQcDocument(
    body: StageQcDocumentPayload,
    options: Record<string, unknown> = {}
  ) {
    const reason =
      body.reasonForChange ??
      body.reason_for_change ??
      "Stage essential document for clinical QC review";
    const wireBody: EtmfSchemas["Etmf_StageDocumentQCRequest"] = {
      content: body.content ?? null,
      contentBase64: body.contentBase64 ?? body.content_base64 ?? null,
      filename: body.filename,
      studyId: body.studyId ?? body.study_id ?? "",
      siteId: body.siteId ?? body.site_id ?? null,
      mimeType: body.mimeType ?? body.mime_type ?? "application/pdf",
      artifactHint: body.artifactHint ?? body.artifact_hint ?? null,
      taxonomyVersion:
        body.taxonomyVersion ?? body.taxonomy_version ?? "v3.2.0-complete",
      reasonForChange: reason,
      assignedCra: body.assignedCra ?? body.assigned_cra ?? null,
    };
    const headers: Record<string, string> = {
      "X-Change-Reason": encodeChangeReason(reason),
    };
    const data = await unwrapResponse(
      etmfClient.POST("/api/v1/etmf/intelligence/stage-qc", {
        body: wireBody,
        headers,
        ...options,
      })
    );
    return normalizeQcStagingItem(data);
  },

  async stageExistingDocumentForQc(
    documentId: string,
    body: StageExistingDocumentQcPayload = {},
    options: Record<string, unknown> = {}
  ) {
    const reason =
      body.reasonForChange ??
      body.reason_for_change ??
      "Promote draft document into technical QC review";
    const encodedReason = encodeChangeReason(reason);
    const wireBody: EtmfSchemas["Etmf_StageExistingDocumentQcRequest"] = {
      reason_for_change: reason,
      assigned_cra: body.assignedCra ?? body.assigned_cra ?? null,
    };
    const data = await unwrapResponse(
      etmfClient.POST("/api/v1/etmf/documents/{document_id}/stage-qc", {
        params: {
          path: { document_id: documentId },
          header: { "X-Change-Reason": encodedReason },
        },
        body: wireBody,
        headers: { "X-Change-Reason": encodedReason },
        ...options,
      })
    );
    return normalizeTmfDocument(data);
  },

  async getQcQueue(
    studyId?: string,
    siteIdOrOptions?: string | null | Record<string, unknown>,
    maybeOptions: Record<string, unknown> = {}
  ) {
    const siteId =
      typeof siteIdOrOptions === "string" ? siteIdOrOptions : undefined;
    const options =
      siteIdOrOptions &&
      typeof siteIdOrOptions === "object" &&
      !Array.isArray(siteIdOrOptions)
        ? siteIdOrOptions
        : maybeOptions;
    const query: { study_id?: string; site_id?: string } = {};
    if (studyId) {
      query.study_id = studyId;
    }
    if (siteId) {
      query.site_id = siteId;
    }
    const data: unknown = await unwrapResponse(
      etmfClient.GET("/api/v1/etmf/intelligence/qc-queue", {
        params: Object.keys(query).length > 0 ? { query } : {},
        ...options,
      })
    );
    return Array.isArray(data)
      ? data.map((item: unknown) => normalizeQcStagingItem(item))
      : data;
  },

  async submitQcReview(
    docId: string,
    body: SubmitQcReviewPayload,
    options: Record<string, unknown> = {}
  ) {
    const reason = body.reasonForChange ?? body.reason_for_change ?? "";
    const wireBody: EtmfSchemas["Etmf_CraqcReviewRequest"] = {
      decision: body.decision,
      reasonForChange: reason,
      overrideArtifactCode:
        body.overrideArtifactCode ?? body.override_artifact_code ?? null,
      discrepancyComment:
        body.discrepancyComment ?? body.discrepancy_comment ?? null,
    };
    const headers: Record<string, string> = {};
    if (reason) {
      headers["X-Change-Reason"] = encodeChangeReason(reason);
    }
    const data = await unwrapResponse(
      etmfClient.POST("/api/v1/etmf/intelligence/qc-review/{doc_id}", {
        params: { path: { doc_id: docId } },
        body: wireBody,
        headers,
        ...options,
      })
    );
    return normalizeTmfDocument(data);
  },

  getTaxonomy(version?: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      etmfClient.GET("/api/v1/etmf/taxonomy", {
        params: version ? { query: { version } } : {},
        ...options,
      })
    );
  },
};
