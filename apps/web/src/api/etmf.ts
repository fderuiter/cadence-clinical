import { buildStepUpVerificationPayload, SEMANTIC_ACTIONS } from "ui";
import { encodeChangeReason } from "./changeReason";
import { openApiClient, unwrapResponse } from "./client";

export interface EtmfDocumentParams {
  study_id?: string;
  zone?: string;
  search?: string;
}

export interface EtmfCompletenessParams {
  study_id?: string;
  milestone?: string;
  site_id?: string;
}

export interface ArchivalStatusParams {
  template_id?: string;
  version_index?: number;
  subject_pseudonym?: string;
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

export interface ExportDocumentsPayload {
  study_id?: string;
  document_ids?: string[];
  zone?: number;
  section?: string;
  artifact_code?: string;
  is_full_binder?: boolean;
}

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

export const etmfService = {
  async exportDocuments(payload: ExportDocumentsPayload) {
    const response = await fetch("/api/v1/etmf/documents/export", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(payload),
    });

    if (response.status === 200) {
      const blob = await response.blob();
      const contentDisposition = response.headers.get("content-disposition");
      let filename = `etmf_export_${payload.study_id || "study"}.zip`;
      if (contentDisposition) {
        const match = contentDisposition.match(/filename="?([^";]+)"?/);
        if (match && match[1]) {
          filename = match[1];
        }
      }
      return { mode: "stream" as const, blob, filename };
    } else if (response.status === 202) {
      const jobData = await response.json();
      return { mode: "job" as const, job: jobData as ExportJobStatus };
    } else {
      const errorText = await response.text();
      throw new Error(`Export failed (${response.status}): ${errorText}`);
    }
  },

  async getExportJob(jobId: string): Promise<ExportJobStatus> {
    const response = await fetch(`/api/v1/etmf/export-jobs/${jobId}`);
    if (!response.ok) {
      throw new Error(`Failed to fetch job status: ${response.statusText}`);
    }
    return response.json();
  },

  async listExportJobs(studyId?: string): Promise<ExportJobStatus[]> {
    const url = studyId
      ? `/api/v1/etmf/export-jobs?study_id=${encodeURIComponent(studyId)}`
      : "/api/v1/etmf/export-jobs";
    const response = await fetch(url);
    if (!response.ok) {
      throw new Error(`Failed to list export jobs: ${response.statusText}`);
    }
    return response.json();
  },
  getDocuments(
    params: EtmfDocumentParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/etmf/documents", {
        params: {
          query: {
            study_id: params.study_id ?? null,
            zone: params.zone ? parseInt(params.zone, 10) : null,
          },
        },
        ...options,
      })
    );
  },

  getDocument(documentId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/etmf/documents/{document_id}", {
        params: { path: { document_id: documentId } },
        ...options,
      })
    );
  },

  ingestDocument(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/ingest", {
        body: body as never,
        ...options,
      })
    );
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

  getArchivalStatus(
    correlationId?: string,
    params: ArchivalStatusParams = {},
    options: Record<string, unknown> = {}
  ) {
    if (correlationId) {
      return unwrapResponse(
        openApiClient.GET("/api/v1/econsent/archival-status/{correlation_id}", {
          params: { path: { correlation_id: correlationId } },
          ...options,
        })
      );
    }
    return unwrapResponse(
      openApiClient.GET("/api/v1/econsent/archival-status", {
        params: {
          query: {
            template_id: (params.template_id ?? null) as unknown as string,
            version_index: params.version_index ?? null,
            subject_pseudonym: (params.subject_pseudonym ??
              null) as unknown as string,
          },
        },
        ...options,
      })
    );
  },

  classifyIntelligence(
    body: {
      filename: string;
      content?: string;
      mime_type?: string;
      content_hint?: string;
      taxonomy_version?: string;
    },
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/intelligence/classify", {
        body: body as never,
        ...options,
      })
    );
  },

  stageQcDocument(
    body: {
      content: string;
      filename: string;
      study_id: string;
      site_id?: string | null;
      mime_type?: string;
      artifact_hint?: string | null;
      taxonomy_version?: string | null;
      reason_for_change: string;
      assigned_cra?: string | null;
    },
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/intelligence/stage-qc", {
        body: body as never,
        ...options,
      })
    );
  },

  getQcQueue(studyId?: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/etmf/intelligence/qc-queue", {
        params: {
          query: {
            study_id: studyId || null,
          },
        },
        ...options,
      })
    );
  },

  submitQcReview(
    docId: string,
    body: {
      decision: string;
      reason_for_change: string;
      override_artifact_code?: string | null;
      discrepancy_comment?: string | null;
    },
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/intelligence/qc-review/{doc_id}", {
        params: { path: { doc_id: docId } },
        body: body as never,
        ...options,
      })
    );
  },

  getTaxonomy(version?: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/etmf/taxonomy", {
        params: {
          query: {
            version,
          },
        },
        ...options,
      })
    );
  },

  autoFile(payload: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/classify", {
        body: payload as never,
        ...options,
      })
    );
  },

  tagDocument(
    documentId: string,
    payload: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/classify", {
        body: { document_id: documentId, ...(payload as object) } as never,
        ...options,
      })
    );
  },

  getInspectionReadiness(
    studyId: string,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/etmf/studies/{study_id}/inspection-readiness",
        {
          params: { path: { study_id: studyId } },
          ...options,
        }
      )
    );
  },

  getEmsExportUrl(
    studyId: string,
    params: { study_title?: string } = {}
  ): string {
    const query = new URLSearchParams();
    if (params.study_title) query.append("study_title", params.study_title);
    const queryString = query.toString();
    return queryString
      ? `/api/v1/etmf/studies/${studyId}/ems-export?${queryString}`
      : `/api/v1/etmf/studies/${studyId}/ems-export`;
  },

  verifyAuditChain(options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/audit-logs/verify-chain", {
        ...options,
      })
    );
  },

  verifyDocumentSignature(
    documentId: string,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/etmf/documents/{document_id}/verify-signature",
        {
          params: { path: { document_id: documentId } },
          ...options,
        }
      )
    );
  },
};
