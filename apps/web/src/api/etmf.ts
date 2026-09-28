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
}

export interface DocumentSignOffPayload {
  signingReason: string;
}

export interface SignOptions {
  changeReason?: string;
  sigToken?: string;
}

export const etmfService = {
  getDocuments(params: EtmfDocumentParams = {}, options: Record<string, unknown> = {}) {
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

  getCompleteness(params: EtmfCompletenessParams = {}, options: Record<string, unknown> = {}) {
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

  verifySignature({ username, password, totp = null, action }: SignatureVerificationPayload, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/execution/signatures/verify", {
        body: {
          username,
          password: password || "",
          totp: totp ?? undefined,
          action,
        } as never,
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
      headers["X-Change-Reason"] = changeReason;
    }

    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/documents/{document_id}/sign-off", {
        params: { path: { document_id: documentId } },
        body: { signing_reason: signingReason as never },
        headers,
      })
    );
  },

  getArchivalStatus(correlationId?: string, params: ArchivalStatusParams = {}, options: Record<string, unknown> = {}) {
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
            subject_pseudonym: (params.subject_pseudonym ?? null) as unknown as string,
          },
        },
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

  tagDocument(documentId: string, payload: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/classify", {
        body: { document_id: documentId, ...(payload as object) } as never,
        ...options,
      })
    );
  },

  getInspectionReadiness(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/etmf/studies/{study_id}/inspection-readiness", {
        params: { path: { study_id: studyId } },
        ...options,
      })
    );
  },

  getEmsExportUrl(studyId: string, params: { study_title?: string } = {}): string {
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

  verifyDocumentSignature(documentId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/etmf/documents/{document_id}/verify-signature", {
        params: { path: { document_id: documentId } },
        ...options,
      })
    );
  },
};
