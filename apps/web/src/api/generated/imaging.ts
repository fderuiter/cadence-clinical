/**
 * Generated from docs/openapi/imaging_openapi.json by scripts/generate-api-types.js.
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
    /** Medical Imaging DICOM service health probe. */
    get: operations["ImagingHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns Medical Imaging operational status. */
    get: operations["ImagingStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/studies": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["StowRsStoreInstances"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/studies/batch": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["BatchIngestStudyFolder"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/studies/{studyUid}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["WadoRsGetStudy"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/studies/{studyUid}/series/{seriesUid}/instances/{instanceUid}/frames/{frameList}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["WadoRsGetInstanceFrames"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/certificates/{certificateId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetDeidCertificate"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/adjudications": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["SubmitOrEvaluateAdjudicationSession"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/adjudications/{sessionId}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get: operations["GetImagingAdjudicationSession"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/adjudications/{sessionId}/chair-adjudicate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["ChairAdjudicateImagingSession"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/imaging/studies/{studyUid}/series/{seriesUid}/segment": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    post: operations["SegmentRoiSeed"];
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
    Imaging_AdjudicationSessionResponse: {
      sessionId: string;
      studyId: string;
      subjectId: string;
      timepointName: string;
      workflowStatus: string;
      isResponseDiscordant: boolean;
      /** Format: double */
      sldDiscordanceDelta: number | string;
      discordanceReasons: string[];
      finalConsensusResponse: null | string;
      /** Format: double */
      finalConsensusSldMm: null | number | string;
      bindingReader: null | string;
      adjudicationRationale: null | string;
      sessionDigest: string;
    };
    Imaging_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Imaging_BatchDeidentificationManifest: {
      manifest_id?: string;
      study_envelope_id?: string;
      study_id?: string;
      site_id?: string;
      subject_id?: string;
      /** Format: int32 */
      total_files_processed?: number | string;
      /** Format: int32 */
      ingested_slice_count?: number | string;
      /** Format: int32 */
      skipped_non_dicom_count?: number | string;
      skipped_non_dicom_files?: string[];
      /** Format: int32 */
      scrubbed_tags_count?: number | string;
      scrubbed_tags?: string[];
      pixel_mask_applied?: boolean;
      pixel_masks?: components["schemas"]["Imaging_PixelMaskBoundingBox"][];
      operator_identity?: string;
      timestamp?: string;
      signature?: null | string;
    };
    Imaging_BatchIngestionResult: {
      studyEnvelopeId: string;
      certificateId: string;
      anonymizedStudyUid: string;
      /** Format: int32 */
      totalFilesReceived: number | string;
      /** Format: int32 */
      ingestedSliceCount: number | string;
      /** Format: int32 */
      skippedNonDicomCount: number | string;
      skippedNonDicomFiles: string[];
      seriesSummary: components["schemas"]["Imaging_SeriesIngestionSummary"][];
      manifest: components["schemas"]["Imaging_BatchDeidentificationManifest"];
      signature: string;
    };
    Imaging_BoundingBoxDto: {
      /** Format: double */
      xMin: number | string;
      /** Format: double */
      yMin: number | string;
      /** Format: double */
      xMax: number | string;
      /** Format: double */
      yMax: number | string;
    };
    Imaging_CertificateResponse: {
      certificate: components["schemas"]["Imaging_DeidentificationCertificate"];
      isSignatureValid: boolean;
    };
    Imaging_ChairAdjudicateImagingRequest: {
      finalConsensusResponse: string;
      /** Format: double */
      finalConsensusSldMm: number | string;
      bindingReader: string;
      adjudicationRationale: string;
      signerName: string;
      signatureMeaning: string;
    };
    Imaging_DeidentificationCertificate: {
      id?: string;
      studyEnvelopeId?: null | string;
      sourceSha256?: string;
      targetSha256?: string;
      studyId?: string;
      siteId?: string;
      subjectId?: string;
      originalStudyInstanceUidHash?: string;
      originalSopInstanceUidHash?: string;
      anonymizedStudyInstanceUid?: string;
      anonymizedSopInstanceUid?: string;
      /** Format: int32 */
      scrubbedTagsCount?: number | string;
      scrubbedTagsJson?: string;
      pixelMaskApplied?: boolean;
      pixelMasksJson?: string;
      operatorIdentity?: string;
      timestamp?: string;
      signature?: string;
      /** Format: date-time */
      createdAt: string;
    };
    Imaging_ImagingAdjudicationSession: {
      id?: string;
      studyEnvelopeId?: null | string;
      studyId?: string;
      siteId?: string;
      subjectId?: string;
      timepointName?: string;
      assessmentCriteria?: string;
      reader1Id?: string;
      reader1Name?: string;
      reader1Status?: string;
      reader1AssessmentJson?: null | string;
      reader2Id?: string;
      reader2Name?: string;
      reader2Status?: string;
      reader2AssessmentJson?: null | string;
      adjudicatorId?: null | string;
      adjudicatorName?: null | string;
      adjudicatorAssessmentJson?: null | string;
      /** Format: double */
      sldDiscordanceDelta?: number | string;
      isResponseDiscordant?: boolean;
      discordanceReasonsJson?: string;
      workflowStatus?: string;
      finalConsensusResponse?: null | string;
      /** Format: double */
      finalConsensusSldMm?: null | number | string;
      bindingReader?: null | string;
      adjudicationRationale?: null | string;
      electronicSignatureJson?: null | string;
      /** Format: date-time */
      lockedAt?: null | string;
      lockedBy?: null | string;
      sessionDigest?: string;
      tenantId?: string;
      /** Format: date-time */
      createdAt: string;
      createdBy: string;
      /** Format: date-time */
      updatedAt: string;
      reasonForChange?: null | string;
      /** Format: int32 */
      versionIndex?: number | string;
    };
    Imaging_ImagingStudyEnvelope: {
      id?: string;
      studyInstanceUid?: string;
      studyId?: string;
      siteId?: string;
      subjectId?: string;
      modalitiesJson?: string;
      studyDate?: null | string;
      studyDescription?: null | string;
      /** Format: int32 */
      seriesCount?: number | string;
      /** Format: int32 */
      instanceCount?: number | string;
      storagePrefix?: null | string;
      status?: string;
      adjudicationStatus?: string;
      tenantId?: string;
      /** Format: date-time */
      createdAt: string;
      createdBy: string;
      /** Format: date-time */
      updatedAt: string;
      reasonForChange?: null | string;
      /** Format: int32 */
      versionIndex?: number | string;
    };
    Imaging_IngestionResult: {
      studyEnvelopeId: string;
      certificateId: string;
      anonymizedStudyUid: string;
      anonymizedSeriesUid: string;
      anonymizedSopUid: string;
      modality: string;
      /** Format: int32 */
      rows: number | string;
      /** Format: int32 */
      columns: number | string;
      /** Format: int32 */
      bitsAllocated: number | string;
      pixelMaskApplied: boolean;
      signature: string;
    };
    Imaging_PixelMaskBoundingBox: {
      /** Format: int32 */
      x_min: number | string;
      /** Format: int32 */
      y_min: number | string;
      /** Format: int32 */
      x_max: number | string;
      /** Format: int32 */
      y_max: number | string;
    };
    Imaging_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Imaging_ReaderAssessmentDto: {
      readerId: string;
      readerName: string;
      /** Format: double */
      sumLongestDiametersMm: number | string;
      targetResponse: string;
      overallResponse: string;
      newLesionsPresent: boolean;
      notes?: null | string[];
    };
    Imaging_SegmentRoiRequest: {
      /** Format: double */
      seedX: number | string;
      /** Format: double */
      seedY: number | string;
      /** Format: int32 */
      sliceIndex?: null | number | string;
      /** @default AXIAL */
      plane: null | string;
      boundingBox?: null | components["schemas"]["Imaging_BoundingBoxDto"];
      lesionId?: null | string;
      organSite?: null | string;
      lesionType?: null | string;
      timepointName?: null | string;
    };
    Imaging_SeriesIngestionSummary: {
      seriesEnvelopeId: string;
      anonymizedSeriesUid: string;
      modality: string;
      /** Format: int32 */
      instanceCount: number | string;
    };
    Imaging_SubmitAdjudicationSessionRequest: {
      studyId: string;
      siteId: string;
      subjectId: string;
      timepointName: string;
      reader1Assessment: components["schemas"]["Imaging_ReaderAssessmentDto"];
      reader2Assessment: components["schemas"]["Imaging_ReaderAssessmentDto"];
      /** @default RECIST_1_1 */
      assessmentCriteria: string;
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
  ImagingHealthCheck: {
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
          "application/json": components["schemas"]["Imaging_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  ImagingStatus: {
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
          "application/json": components["schemas"]["Imaging_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  StowRsStoreInstances: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Imaging_IngestionResult"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Payload Too Large */
      413: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
  BatchIngestStudyFolder: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody?: never;
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Imaging_BatchIngestionResult"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Payload Too Large */
      413: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
  WadoRsGetStudy: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyUid: string;
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
          "application/json": components["schemas"]["Imaging_ImagingStudyEnvelope"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
  WadoRsGetInstanceFrames: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyUid: string;
        seriesUid: string;
        instanceUid: string;
        frameList: string;
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
        content?: never;
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
  GetDeidCertificate: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        certificateId: string;
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
          "application/json": components["schemas"]["Imaging_CertificateResponse"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
  SubmitOrEvaluateAdjudicationSession: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Imaging_SubmitAdjudicationSessionRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Imaging_AdjudicationSessionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
  GetImagingAdjudicationSession: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        sessionId: string;
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
          "application/json": components["schemas"]["Imaging_ImagingAdjudicationSession"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
  ChairAdjudicateImagingSession: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        sessionId: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Imaging_ChairAdjudicateImagingRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Imaging_AdjudicationSessionResponse"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Unauthorized */
      401: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
  SegmentRoiSeed: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        studyUid: string;
        seriesUid: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Imaging_SegmentRoiRequest"];
      };
    };
    responses: {
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
      /** @description Not Implemented */
      501: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Imaging_ProblemDetails"];
        };
      };
    };
  };
}
