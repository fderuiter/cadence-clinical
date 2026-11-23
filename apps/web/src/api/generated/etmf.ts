/**
 * Generated from docs/openapi/etmf_openapi.json by scripts/generate-api-types.js.
 * Do not edit; run `pnpm --filter web generate:api` after regenerating the spec.
 */

export interface paths {
  "/healthz": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** eTMF operations health probe. */
    get: operations["EtmfHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns eTMF operational status. */
    get: operations["EtmfStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/intelligence/classify": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Classifies a clinical document against the DIA TMF Reference Model v3.2 taxonomy. */
    post: operations["ClassifyEtmfDocument"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/intelligence/stage-qc": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Uploads, classifies, and stages a trial document into the CRA Technical QC queue. */
    post: operations["StageEtmfDocumentForQc"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/intelligence/qc-queue": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists trial documents currently staged in TECHNICAL_QC for CRA review. */
    get: operations["ListEtmfQcQueue"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/intelligence/qc-review/{doc_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Executes a CRA Technical QC decision (ACCEPT, OVERRIDE, or REJECT) on a staged document. */
    post: operations["ReviewEtmfQcDocument"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/documents": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists eTMF documents filtered by study, site, or DIA TMF zone. */
    get: operations["ListEtmfDocuments"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/documents/{document_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieves an eTMF document metadata record by document ID. */
    get: operations["GetEtmfDocumentById"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/documents/{document_id}/content": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Streams SHA-256 integrity-verified binary content for an eTMF document. */
    get: operations["DownloadEtmfDocumentContent"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/documents/{document_id}/submit-qc": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Submits a DRAFT eTMF document into TECHNICAL_QC for classification review. */
    post: operations["SubmitEtmfDocumentForQc"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/documents/{document_id}/taxonomy": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    /** Updates the DIA TMF zone, section, and artifact classification of an eTMF document. */
    patch: operations["UpdateEtmfDocumentTaxonomy"];
    trace?: never;
  };
  "/api/v1/etmf/documents/export": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Streams a synchronous study binder ZIP archive or enqueues an asynchronous bulk export job. */
    post: operations["ExportEtmfDocuments"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/export-jobs/{job_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Retrieves progress and download metadata for a background eTMF binder export job. */
    get: operations["GetEtmfExportJob"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/export-jobs/{job_id}/download": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Downloads the completed study binder ZIP archive for an eTMF export job. */
    get: operations["DownloadEtmfExportJob"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/export-jobs": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Lists eTMF study binder export jobs visible to the caller. */
    get: operations["ListEtmfExportJobs"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/ingest": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Ingests a new draft eTMF document with SHA-256 checksum and revision lineage. */
    post: operations["IngestEtmfDocument"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/etmf/taxonomy": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns the supported DIA TMF Reference Model v3.2 zone, section, and artifact catalog. */
    get: operations["GetEtmfTaxonomy"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
}
export type webhooks = Record<string, never>;
export interface components {
  schemas: {
    Etmf_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Etmf_ClassifyRequest: {
      filename: string;
      content?: null | string;
      /** @default application/pdf */
      mimeType: null | string;
      contentHint?: null | string;
      /** @default v3.2.0-complete */
      taxonomyVersion: null | string;
    };
    Etmf_CraqcReviewRequest: {
      decision: string;
      reasonForChange: string;
      overrideArtifactCode?: null | string;
      discrepancyComment?: null | string;
    };
    Etmf_CraqcStagingItem: {
      documentId: string;
      studyId: string;
      siteId: null | string;
      filename: string;
      status: string;
      intelligenceReport: components["schemas"]["Etmf_DocumentIntelligenceReport"];
      /** Format: date-time */
      stagedAt: string;
      stagedBy: string;
      qcAssignedTo: null | string;
      discrepancyNotes: string[];
    };
    Etmf_DocumentIntelligenceReport: {
      documentId: null | string;
      filename: string;
      mimeType: string;
      sha256Hash: string;
      modality: string;
      primaryClassification: components["schemas"]["Etmf_TaxonomyMatchCandidate"];
      alternativeCandidates: components["schemas"]["Etmf_TaxonomyMatchCandidate"][];
      confidenceTier: string;
      qcRecommendation: string;
      aiGenerationManifest: Record<string, never>;
    };
    Etmf_EtmfExportJobAcceptedResponse: {
      job_id: string;
      study_id: components["schemas"]["Etmf_StudyId"];
      requested_by: string;
      status: string;
      /** Format: double */
      progress_percentage: number | string;
      /** Format: int32 */
      total_documents: number | string;
      /** Format: int32 */
      processed_documents: number | string;
      download_url: null | string;
      /** Format: date-time */
      expires_at: null | string;
      /** Format: date-time */
      created_at: string;
      status_url: string;
      message: string;
    };
    Etmf_EtmfExportJobStatusDto: {
      job_id: string;
      study_id: components["schemas"]["Etmf_StudyId"];
      requested_by: string;
      status: string;
      /** Format: double */
      progress_percentage: number | string;
      /** Format: int32 */
      total_documents: number | string;
      /** Format: int32 */
      processed_documents: number | string;
      download_url: null | string;
      /** Format: date-time */
      expires_at: null | string;
      /** Format: date-time */
      created_at: string;
    };
    Etmf_EtmfTaxonomyEntryDto: {
      /** Format: int32 */
      zone: number | string;
      zone_name: string;
      section: string;
      section_name: string;
      artifact_code: string;
      artifact_name: string;
    };
    Etmf_ExportDocumentsRequest: {
      study_id?: null | string;
      document_ids?: null | string[];
      /** Format: int32 */
      zone?: null | number | string;
      section?: null | string;
      artifact_code?: null | string;
      /** @default false */
      is_full_binder: boolean;
    };
    Etmf_IngestDocumentRequest: {
      filename: string;
      content: null | string;
      studyId: null | string;
      siteId: null | string;
      mimeType: null | string;
      /** Format: int32 */
      zone: number | string;
      section: null | string;
      artifactCode: null | string;
      artifactType: null | string;
      taxonomyVersion: null | string;
      reasonForChange: null | string;
      contentBase64?: null | string;
    };
    Etmf_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Etmf_StageDocumentQCRequest: {
      content: null | string;
      filename: string;
      studyId: string;
      siteId?: null | string;
      /** @default application/pdf */
      mimeType: null | string;
      artifactHint?: null | string;
      /** @default v3.2.0-complete */
      taxonomyVersion: null | string;
      /** @default Batch upload AI auto-classification staging for CRA QC */
      reasonForChange: string;
      assignedCra?: null | string;
      contentBase64?: null | string;
    };
    Etmf_StudyId: unknown;
    Etmf_SubmitDocumentForQcRequest: {
      reasonForChange?: null | string;
      assignedCra?: null | string;
    };
    Etmf_TaxonomyMatchCandidate: {
      /** Format: int32 */
      zone: number | string;
      section: string;
      artifactCode: string;
      artifactType: string;
      /** Format: double */
      confidence: number | string;
    };
    Etmf_TmfDocument: {
      studyId?: string;
      idempotencyKey?: null | string;
      siteId?: null | string;
      /** Format: int32 */
      zone?: number | string;
      section?: string;
      artifactType?: string;
      filename?: string;
      content?: string;
      mimeType?: string;
      status?: string;
      taxonomyVersion?: string;
      artifactCode?: string;
      metadataJson?: null | string;
      /** Format: date-time */
      issueDate?: null | string;
      /** Format: date-time */
      expirationDate?: null | string;
      documentOwnerId?: null | string;
      protocolVersionTag?: null | string;
      /** Format: int32 */
      protocolVersionIndex?: null | number | string;
      protocolVersionStatus?: null | string;
      documentType?: null | string;
      approvalStatus?: string;
      signatureManifestation?: null | string;
      signer?: null | string;
      /** Format: date-time */
      signingTimestamp?: null | string;
      isRedacted?: boolean;
      redactionSourceId?: null | string;
      redactionManifestJson?: null | string;
      correlationKey?: null | string;
      contentChecksum?: null | string;
      sourceSystem?: null | string;
      syncStatus?: null | string;
      objectKey?: null | string;
      /** Format: int32 */
      transitionSequence?: number | string;
      transitions?:
        null | components["schemas"]["Etmf_TmfDocumentQcTransition"][];
      id?: null | string;
      /** Format: date-time */
      createdAt?: string;
      createdBy?: string;
      reasonForChange?: string;
      /** Format: int64 */
      versionIndex?: number | string;
    };
    Etmf_TmfDocumentQcTransition: {
      id?: string;
      documentId?: string;
      /** Format: int32 */
      transitionSequence?: number | string;
      fromStatus?: string;
      toStatus?: string;
      actorId?: string;
      actorRole?: string;
      reasonForChange?: string;
      /** Format: date-time */
      timestamp: string;
    };
    Etmf_UpdateDocumentTaxonomyRequest: {
      /** Format: int32 */
      zone?: null | number | string;
      section?: null | string;
      artifactCode?: null | string;
      artifactType?: null | string;
      reasonForChange?: null | string;
    };
  };
  responses: never;
  parameters: never;
  requestBodies: never;
  headers: never;
  pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
  EtmfHealthCheck: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  EtmfStatus: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  ClassifyEtmfDocument: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Etmf_ClassifyRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_DocumentIntelligenceReport"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  StageEtmfDocumentForQc: {
    parameters: {
      query?: never;
      header?: {
        /** @description Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Etmf_StageDocumentQCRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_CraqcStagingItem"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  ListEtmfQcQueue: {
    parameters: {
      query?: {
        study_id?: string;
        site_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_CraqcStagingItem"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  ReviewEtmfQcDocument: {
    parameters: {
      query?: never;
      header?: {
        /** @description Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path: {
        doc_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Etmf_CraqcReviewRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_TmfDocument"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  ListEtmfDocuments: {
    parameters: {
      query?: {
        study_id?: string;
        zone?: number | string;
        site_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_TmfDocument"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  GetEtmfDocumentById: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        document_id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_TmfDocument"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  DownloadEtmfDocumentContent: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        document_id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/pdf": string;
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  SubmitEtmfDocumentForQc: {
    parameters: {
      query?: never;
      header?: {
        /** @description Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path: {
        document_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Etmf_SubmitDocumentForQcRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_TmfDocument"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  UpdateEtmfDocumentTaxonomy: {
    parameters: {
      query?: never;
      header?: {
        /** @description Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path: {
        document_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Etmf_UpdateDocumentTaxonomyRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_TmfDocument"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  ExportEtmfDocuments: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Etmf_ExportDocumentsRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/zip": string;
        };
      };
      /** @description Accepted */
      202: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_EtmfExportJobAcceptedResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  GetEtmfExportJob: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        job_id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_EtmfExportJobStatusDto"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  DownloadEtmfExportJob: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        job_id: string;
      };
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/zip": string;
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Gone */
      410: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  ListEtmfExportJobs: {
    parameters: {
      query?: {
        study_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_EtmfExportJobStatusDto"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  IngestEtmfDocument: {
    parameters: {
      query?: never;
      header?: {
        /** @description Optional 21 CFR Part 11 reason for change (ADR-2495): printable ASCII or RFC 8187 UTF-8'' percent-encoded UTF-8. */
        "X-Change-Reason"?: string;
      };
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Etmf_IngestDocumentRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_TmfDocument"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
  GetEtmfTaxonomy: {
    parameters: {
      query?: {
        version?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Etmf_EtmfTaxonomyEntryDto"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Etmf_ProblemDetails"];
        };
      };
    };
  };
}
