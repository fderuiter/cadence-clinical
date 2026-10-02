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

export const etmfService = {
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
        semantic = "econsent.consent.capture";
      } else if (action.includes("sign-off")) {
        semantic = "etmf.document.sign_off";
      } else if (action.includes("chair-adjudicate")) {
        semantic = "execution.cec.chair_adjudicate";
      } else {
        semantic = "execution.signature.verify";
      }
    }

    return unwrapResponse(
      (openApiClient as any).POST("/api/v1/auth/signature-verification", {
        body: {
          username,
          password: password || "",
          totp: totp ?? undefined,
          action,
          semantic_action: semantic,
        },
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

  bulkTagDocuments(
    payload: {
      document_ids: string[];
      reason_for_change: string;
      zone?: number | null;
      section?: string | null;
      artifact_code?: string | null;
      artifact_type?: string | null;
      site_id?: string | null;
      milestone?: string | null;
    },
    options: Record<string, unknown> = {}
  ) {
    const headers: Record<string, string> = {};
    if (payload.reason_for_change) {
      headers["X-Change-Reason"] = encodeChangeReason(payload.reason_for_change);
    }
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/documents/bulk-tag" as any, {
        body: payload as never,
        headers,
        ...options,
      })
    );
  },
};
