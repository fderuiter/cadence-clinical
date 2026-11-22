/**
 * Generated from docs/openapi/ctms_openapi.json by scripts/generate-api-types.js.
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
    /** CTMS operations health probe. */
    get: operations["CtmsHealthCheck"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/status": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Returns CTMS operational status. */
    get: operations["CtmsStatus"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/letters/render": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Renders a CTMS site visit monitoring letter via Liquid template. */
    post: operations["RenderSiteVisitLetter"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/letters/compile": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Compiles a GxP-compliant PDF visit letter with Part 11 electronic signature, eTMF filing, and automated email dispatch. */
    post: operations["CompileSiteVisitLetterPdf"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/letters/{letterId}/pdf": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Downloads or views the compiled GxP PDF visit letter. */
    get: operations["GetSiteVisitLetterPdf"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/monitoring-visits": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List monitoring visits filtered by study, site, CRA, or status. */
    get: operations["ListMonitoringVisits"];
    put?: never;
    /** Schedule a new monitoring visit. */
    post: operations["ScheduleMonitoringVisit"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/monitoring-visits/{visit_id}/complete": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Complete a scheduled monitoring visit with actual date and clinical findings. */
    post: operations["CompleteMonitoringVisit"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/monitoring-visits/{visit_id}/sign-off": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Perform supervisory Part 11 sign-off on a completed monitoring visit. */
    post: operations["SignOffMonitoringVisit"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/monitoring-visits/{visit_id}/letters": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Get generated letters for a monitoring visit. */
    get: operations["GetMonitoringVisitLetters"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Get a generated visit letter by type. */
    get: operations["GetMonitoringVisitLetterByType"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/site-milestones": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** List site milestones filtered by study or site. */
    get: operations["ListSiteMilestones"];
    put?: never;
    /** Create a new site milestone. */
    post: operations["CreateSiteMilestone"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/site-milestones/{milestone_id}": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    /** Update site milestone status, planned date, or actual date. */
    put: operations["UpdateSiteMilestone"];
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/startup/sites/{siteId}/greenlight": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Evaluates 6-facet greenlight readiness for a single trial site. */
    get: operations["EvaluateSiteGreenlight"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/startup/sites/{siteId}/greenlight/certify": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Certifies greenlight activation for a single trial site, issuing an HMAC-SHA256 token. */
    post: operations["CertifySiteGreenlight"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/startup/matrix": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Displays country-level matrix showing 6-facet greenlight readiness for all sites in a selected country. */
    get: operations["GetCountryGreenlightMatrix"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/startup/matrix/batch-certify": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Batch-certifies all selected 100% compliant sites in a target country with a single e-signature. */
    post: operations["BatchCertifyCountrySites"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/doa/delegate": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Assign site trial task delegation requiring Principal Investigator sign-off. */
    post: operations["DelegateSiteTasks"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/doa/revoke": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Revoke or end a delegated trial duty with reason for change. */
    post: operations["RevokeSiteTasks"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/doa/sign-off": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    get?: never;
    put?: never;
    /** Endorse Delegation of Authority task assignment with Principal Investigator eSignature. */
    post: operations["SignOffDelegation"];
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/doa/sites/{site_id}/log": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Fetch active and historical DOA log matrix for a site. */
    get: operations["GetSiteDoaLog"];
    put?: never;
    post?: never;
    delete?: never;
    options?: never;
    head?: never;
    patch?: never;
    trace?: never;
  };
  "/api/v1/ctms/doa/sites/{site_id}/export-pdf": {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    /** Export 21 CFR Part 11 signed DOA PDF log. */
    get: operations["ExportSiteDoaPdf"];
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
    Ctms_AnonymousTypeOfstringAndstring: {
      status: null | string;
      service: null | string;
    };
    Ctms_BatchCertifyRequestDto: {
      study_id: string;
      country_code: string;
      site_ids: string[];
      meaning: null | string;
    };
    Ctms_BatchCertifyResponseDto: {
      success: boolean;
      /** Format: int32 */
      certified_count: number | string;
      study_id: string;
      country_code: string;
      certified_sites: components["schemas"]["Ctms_CertifiedSiteItemDto"][];
      message: string;
    };
    Ctms_CertifiedSiteItemDto: {
      site_id: string;
      site_name: string;
      greenlight_token: string;
      certified_at: string;
      certified_by: string;
      status: string;
    };
    Ctms_DelegationResponseDto: {
      record_id: string;
      site_id: string;
      staff_user_id: string;
      task_codes: string[];
      start_date: string;
      end_date: null | string;
      is_active: boolean;
      signed_off: boolean;
      reason_for_change: string;
      created_by: string;
      /** Format: int64 */
      version_index: number | string;
      kernel_grant_ids?: null | string[];
    };
    Ctms_DelegationTaskRequest: {
      site_id: string;
      staff_user_id: null | string;
      task_codes: string[];
      start_date: string;
      reason_for_change: null | string;
      staff_email?: null | string;
      staff_name?: null | string;
      end_date?: null | string;
      study_id?: null | string;
    };
    Ctms_DoaAuditHistoryItemDto: {
      id: string;
      /** Format: date-time */
      timestamp: string;
      user_id: string;
      user_role: string;
      action: string;
      details: string;
    };
    Ctms_DoaLogResponseDto: {
      site_id: string;
      pi_name: string;
      delegated_staff: components["schemas"]["Ctms_DelegationResponseDto"][];
      audit_history: components["schemas"]["Ctms_DoaAuditHistoryItemDto"][];
    };
    Ctms_DoaSignOffRequest: {
      record_id: string;
      reason_for_change: null | string;
      study_id?: null | string;
    };
    Ctms_FindingCreateDto: {
      text: string;
      severity: string;
      resolution_status?: null | string;
    };
    Ctms_GeneratedLetterResponseDto: {
      id: string;
      visit_id: string;
      letter_type: string;
      rendered_content: string;
      created_at: string;
      created_by: string;
      reason_for_change: string;
      /** Format: int64 */
      version_index: number | string;
    };
    Ctms_MonitoringVisitCompleteDto: {
      /** Format: date-time */
      actual_date: string;
      findings?: null | components["schemas"]["Ctms_FindingCreateDto"][];
      reason_for_change?: null | string;
    };
    Ctms_MonitoringVisitCreateDto: {
      study_id: string;
      site_id: string;
      cra_id: string;
      visit_type: string;
      /** Format: date-time */
      scheduled_date: string;
      reason_for_change?: null | string;
    };
    Ctms_MonitoringVisitResponseDto: {
      id: string;
      study_id: string;
      site_id: string;
      cra_id: string;
      visit_type: string;
      scheduled_date: string;
      actual_date: null | string;
      status: string;
      created_at: string;
      created_by: string;
      reason_for_change: string;
      /** Format: int64 */
      version_index: number | string;
      signature_manifestation?: null | string;
      signer?: null | string;
      signing_timestamp?: null | string;
    };
    Ctms_ProblemDetails: {
      type?: null | string;
      title?: null | string;
      /** Format: int32 */
      status?: null | number | string;
      detail?: null | string;
      instance?: null | string;
    };
    Ctms_RevokeDelegationRequest: {
      record_id: string;
      reason_for_change: null | string;
      study_id?: null | string;
    };
    Ctms_SiteGreenlightGateResponseDto: {
      id: string;
      study_id: string;
      site_id: string;
      site_name: string;
      country_code: string;
      overall_status: string;
      /** Format: int32 */
      readiness_percentage: number | string;
      facet_regulatory_irb: boolean;
      facet_cta_budget: boolean;
      facet_eisf_baseline: boolean;
      facet_doa_log: boolean;
      facet_staff_training: boolean;
      facet_ip_release: boolean;
      is_compliant: boolean;
      greenlight_token: null | string;
      greenlight_certified_by: null | string;
      greenlight_certified_at: null | string;
      is_overridden: boolean;
      unfulfilled_reasons: null | string[];
    };
    Ctms_SiteMilestoneCreateDto: {
      site_id: string;
      study_id: string;
      milestone_type: string;
      /** Format: date-time */
      planned_date?: null | string;
      /** Format: date-time */
      actual_date?: null | string;
      status?: null | string;
      reason_for_change?: null | string;
    };
    Ctms_SiteMilestoneResponseDto: {
      id: string;
      site_id: string;
      study_id: string;
      milestone_type: string;
      planned_date: null | string;
      actual_date: null | string;
      status: string;
      created_at: string;
      created_by: string;
      reason_for_change: string;
      /** Format: int64 */
      version_index: number | string;
    };
    Ctms_SiteMilestoneUpdateDto: {
      /** Format: date-time */
      planned_date?: null | string;
      /** Format: date-time */
      actual_date?: null | string;
      status?: null | string;
      reason_for_change?: null | string;
    };
    Ctms_SiteVisitFindingModel: {
      text: string;
      severity: string;
      resolutionStatus: string;
    };
    Ctms_SiteVisitLetterModel: {
      craId: string;
      createdAt: string;
      studyId: string;
      siteId: string;
      /** @default Monitoring */
      visitType: string;
      scheduledDate?: null | string;
      actualDate?: null | string;
      findings?: null | components["schemas"]["Ctms_SiteVisitFindingModel"][];
      visitId?: null | string;
      /** @default false */
      finalize: boolean;
      signerName?: null | string;
      signingPurpose?: null | string;
      investigatorEmail?: null | string;
      reasonForChange?: null | string;
    };
    Ctms_VisitLetterCompilationResultDto: {
      letterId: string;
      visitId: string;
      letterType: string;
      pdfSha256Hash: string;
      pdfObjectKey: string;
      isSigned: boolean;
      status: string;
      signatureManifestation: string;
      etmfDocumentId: null | string;
      /** Format: date-time */
      dispatchedAt: null | string;
      pdfAccessUrl: string;
      pdfBase64: string;
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
  CtmsHealthCheck: {
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
          "application/json": components["schemas"]["Ctms_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  CtmsStatus: {
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
          "application/json": components["schemas"]["Ctms_AnonymousTypeOfstringAndstring"];
        };
      };
    };
  };
  RenderSiteVisitLetter: {
    parameters: {
      query: {
        templateName: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_SiteVisitLetterModel"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "text/plain": string;
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  CompileSiteVisitLetterPdf: {
    parameters: {
      query: {
        templateName: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_SiteVisitLetterModel"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_VisitLetterCompilationResultDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  GetSiteVisitLetterPdf: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        letterId: string;
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
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  ListMonitoringVisits: {
    parameters: {
      query?: {
        study_id?: string;
        site_id?: string;
        cra_id?: string;
        status?: string;
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
          "application/json": components["schemas"]["Ctms_MonitoringVisitResponseDto"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  ScheduleMonitoringVisit: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_MonitoringVisitCreateDto"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_MonitoringVisitResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  CompleteMonitoringVisit: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        visit_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_MonitoringVisitCompleteDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_MonitoringVisitResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  SignOffMonitoringVisit: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        visit_id: string;
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
          "application/json": components["schemas"]["Ctms_MonitoringVisitResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  GetMonitoringVisitLetters: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        visit_id: string;
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
          "application/json": components["schemas"]["Ctms_GeneratedLetterResponseDto"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  GetMonitoringVisitLetterByType: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        visit_id: string;
        letter_type: string;
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
          "application/json": components["schemas"]["Ctms_GeneratedLetterResponseDto"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  ListSiteMilestones: {
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
          "application/json": components["schemas"]["Ctms_SiteMilestoneResponseDto"][];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  CreateSiteMilestone: {
    parameters: {
      query?: never;
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_SiteMilestoneCreateDto"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_SiteMilestoneResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  UpdateSiteMilestone: {
    parameters: {
      query?: never;
      header?: never;
      path: {
        milestone_id: string;
      };
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_SiteMilestoneUpdateDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_SiteMilestoneResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  EvaluateSiteGreenlight: {
    parameters: {
      query: {
        study_id: string;
        country_code?: string;
      };
      header?: never;
      path: {
        siteId: string;
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
          "application/json": components["schemas"]["Ctms_SiteGreenlightGateResponseDto"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  CertifySiteGreenlight: {
    parameters: {
      query: {
        study_id: string;
        country_code?: string;
      };
      header?: {
        "X-Change-Reason"?: string;
      };
      path: {
        siteId: string;
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
          "application/json": components["schemas"]["Ctms_SiteGreenlightGateResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  GetCountryGreenlightMatrix: {
    parameters: {
      query: {
        study_id: string;
        country_code: string;
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
          "application/json": components["schemas"]["Ctms_SiteGreenlightGateResponseDto"][];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  BatchCertifyCountrySites: {
    parameters: {
      query?: never;
      header?: {
        "X-Change-Reason"?: string;
      };
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_BatchCertifyRequestDto"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_BatchCertifyResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  DelegateSiteTasks: {
    parameters: {
      query?: {
        study_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_DelegationTaskRequest"];
      };
    };
    responses: {
      /** @description Created */
      201: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_DelegationResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Conflict */
      409: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  RevokeSiteTasks: {
    parameters: {
      query?: {
        study_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_RevokeDelegationRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_DelegationResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Unprocessable Entity */
      422: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  SignOffDelegation: {
    parameters: {
      query?: {
        study_id?: string;
      };
      header?: never;
      path?: never;
      cookie?: never;
    };
    requestBody: {
      content: {
        "application/json": components["schemas"]["Ctms_DoaSignOffRequest"];
      };
    };
    responses: {
      /** @description OK */
      200: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/json": components["schemas"]["Ctms_DelegationResponseDto"];
        };
      };
      /** @description Bad Request */
      400: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  GetSiteDoaLog: {
    parameters: {
      query?: {
        study_id?: string;
      };
      header?: never;
      path: {
        site_id: string;
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
          "application/json": components["schemas"]["Ctms_DoaLogResponseDto"];
        };
      };
      /** @description Forbidden */
      403: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
  ExportSiteDoaPdf: {
    parameters: {
      query?: {
        study_id?: string;
      };
      header?: never;
      path: {
        site_id: string;
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
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
      /** @description Not Found */
      404: {
        headers: {
          [name: string]: unknown;
        };
        content: {
          "application/problem+json": components["schemas"]["Ctms_ProblemDetails"];
        };
      };
    };
  };
}
