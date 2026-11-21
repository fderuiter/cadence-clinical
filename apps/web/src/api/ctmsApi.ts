import { encodeChangeReason } from "./changeReason";
import { createOpenApiClient, unwrapResponse } from "./client";
import type { components, paths } from "./generated/ctms";

type Schemas = components["schemas"];

export type SiteGreenlightGateResponse =
  Schemas["Ctms_SiteGreenlightGateResponseDto"];
export type BatchCertifyRequest = Schemas["Ctms_BatchCertifyRequestDto"];
export type BatchCertifyResponse = Schemas["Ctms_BatchCertifyResponseDto"];
export type CertifiedSiteItem = Schemas["Ctms_CertifiedSiteItemDto"];

export type SiteMilestoneResponse = Schemas["Ctms_SiteMilestoneResponseDto"];
export type SiteMilestoneCreate = Schemas["Ctms_SiteMilestoneCreateDto"];
export type SiteMilestoneUpdate = Schemas["Ctms_SiteMilestoneUpdateDto"];

export type MonitoringVisitResponse =
  Schemas["Ctms_MonitoringVisitResponseDto"];
export type MonitoringVisitCreate = Schemas["Ctms_MonitoringVisitCreateDto"];
export type MonitoringVisitComplete =
  Schemas["Ctms_MonitoringVisitCompleteDto"];
export type FindingCreate = Schemas["Ctms_FindingCreateDto"];
export type GeneratedLetterResponse =
  Schemas["Ctms_GeneratedLetterResponseDto"];

export type SiteVisitFindingModel = Schemas["Ctms_SiteVisitFindingModel"];
export type SiteVisitLetterModel = Schemas["Ctms_SiteVisitLetterModel"];
export type VisitLetterCompilationResult =
  Schemas["Ctms_VisitLetterCompilationResultDto"];

export type DelegationResponse = Schemas["Ctms_DelegationResponseDto"];
export type DelegationTaskRequest = Schemas["Ctms_DelegationTaskRequest"];
export type RevokeDelegationRequest = Schemas["Ctms_RevokeDelegationRequest"];
export type DoaSignOffRequest = Schemas["Ctms_DoaSignOffRequest"];
export type DoaLogResponse = Schemas["Ctms_DoaLogResponseDto"];

export interface ListSiteMilestonesQuery {
  study_id?: string;
  site_id?: string;
}

export interface ListMonitoringVisitsQuery {
  study_id?: string;
  site_id?: string;
  cra_id?: string;
  status?: string;
}

export interface SignedMutationOptions {
  changeReason?: string;
  sigToken?: string;
}

function buildAuditHeaders(
  options: SignedMutationOptions = {}
): Record<string, string> {
  const headers: Record<string, string> = {};
  if (options.changeReason) {
    headers["X-Change-Reason"] = encodeChangeReason(options.changeReason);
  }
  if (options.sigToken) {
    headers["X-Sig-Token"] = options.sigToken;
  }
  return headers;
}

const client = createOpenApiClient<paths>();

/**
 * Typed client for the .NET CTMS service, generated from docs/openapi/ctms_openapi.json.
 * Every request flows through `createOpenApiClient` so `Authorization: Bearer` and
 * `X-Change-Reason` headers are attached consistently.
 */
export const ctmsApi = {
  /** Evaluates 6-facet greenlight readiness for a single trial site (GET /api/v1/ctms/startup/sites/{siteId}/greenlight). */
  evaluateSiteGreenlight(
    siteId: string,
    studyId: string,
    countryCode?: string
  ): Promise<SiteGreenlightGateResponse> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/startup/sites/{siteId}/greenlight", {
        params: {
          path: { siteId },
          query: {
            study_id: studyId,
            country_code: countryCode,
          },
        },
      })
    );
  },

  /** Certifies greenlight activation for a single trial site (POST /api/v1/ctms/startup/sites/{siteId}/greenlight/certify). */
  certifySiteGreenlight(
    siteId: string,
    studyId: string,
    reasonForChange: string,
    countryCode?: string,
    sigToken?: string
  ): Promise<SiteGreenlightGateResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/startup/sites/{siteId}/greenlight/certify", {
        params: {
          path: { siteId },
          query: {
            study_id: studyId,
            country_code: countryCode,
          },
        },
        headers: buildAuditHeaders({
          changeReason: reasonForChange,
          sigToken,
        }),
      })
    );
  },

  /** Fetches the country-level 6-facet greenlight matrix (GET /api/v1/ctms/startup/matrix). */
  getCountryMatrix(
    studyId: string,
    countryCode: string
  ): Promise<SiteGreenlightGateResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/startup/matrix", {
        params: {
          query: {
            study_id: studyId,
            country_code: countryCode,
          },
        },
      })
    );
  },

  /** Batch-certifies 100% compliant sites in a target country (POST /api/v1/ctms/startup/matrix/batch-certify). */
  batchCertifyCountrySites(
    payload: BatchCertifyRequest,
    reasonForChange: string,
    sigToken?: string
  ): Promise<BatchCertifyResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/startup/matrix/batch-certify", {
        body: payload,
        headers: buildAuditHeaders({
          changeReason: reasonForChange,
          sigToken,
        }),
      })
    );
  },

  /** Lists site milestones filtered by study or site (GET /api/v1/ctms/site-milestones). */
  listSiteMilestones(
    query: ListSiteMilestonesQuery = {}
  ): Promise<SiteMilestoneResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/site-milestones", {
        params: {
          query: {
            study_id: query.study_id,
            site_id: query.site_id,
          },
        },
      })
    );
  },

  /** Creates a new site milestone (POST /api/v1/ctms/site-milestones). */
  createSiteMilestone(
    payload: SiteMilestoneCreate
  ): Promise<SiteMilestoneResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/site-milestones", {
        body: payload,
        headers: buildAuditHeaders({
          changeReason: payload.reason_for_change ?? undefined,
        }),
      })
    );
  },

  /** Updates an existing site milestone (PUT /api/v1/ctms/site-milestones/{milestone_id}). */
  updateSiteMilestone(
    milestoneId: string,
    payload: SiteMilestoneUpdate
  ): Promise<SiteMilestoneResponse> {
    return unwrapResponse(
      client.PUT("/api/v1/ctms/site-milestones/{milestone_id}", {
        params: { path: { milestone_id: milestoneId } },
        body: payload,
        headers: buildAuditHeaders({
          changeReason: payload.reason_for_change ?? undefined,
        }),
      })
    );
  },

  /** Lists monitoring visits filtered by study, site, CRA, or status (GET /api/v1/ctms/monitoring-visits). */
  listMonitoringVisits(
    query: ListMonitoringVisitsQuery = {}
  ): Promise<MonitoringVisitResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/monitoring-visits", {
        params: {
          query: {
            study_id: query.study_id,
            site_id: query.site_id,
            cra_id: query.cra_id,
            status: query.status,
          },
        },
      })
    );
  },

  /** Schedules a new monitoring visit (POST /api/v1/ctms/monitoring-visits). */
  scheduleMonitoringVisit(
    payload: MonitoringVisitCreate
  ): Promise<MonitoringVisitResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits", {
        body: payload,
        headers: buildAuditHeaders({
          changeReason: payload.reason_for_change ?? undefined,
        }),
      })
    );
  },

  /** Completes a scheduled monitoring visit with actual date and clinical findings (POST /api/v1/ctms/monitoring-visits/{visit_id}/complete). */
  completeMonitoringVisit(
    visitId: string,
    payload: MonitoringVisitComplete
  ): Promise<MonitoringVisitResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits/{visit_id}/complete", {
        params: { path: { visit_id: visitId } },
        body: payload,
        headers: buildAuditHeaders({
          changeReason: payload.reason_for_change ?? undefined,
        }),
      })
    );
  },

  /** Performs supervisory Part 11 sign-off on a completed monitoring visit (POST /api/v1/ctms/monitoring-visits/{visit_id}/sign-off). */
  signOffMonitoringVisit(
    visitId: string,
    reasonForChange: string,
    sigToken?: string
  ): Promise<MonitoringVisitResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits/{visit_id}/sign-off", {
        params: { path: { visit_id: visitId } },
        headers: buildAuditHeaders({
          changeReason: reasonForChange,
          sigToken,
        }),
      })
    );
  },

  /** Lists generated visit letters for a monitoring visit (GET /api/v1/ctms/monitoring-visits/{visit_id}/letters). */
  getMonitoringVisitLetters(
    visitId: string
  ): Promise<GeneratedLetterResponse[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/monitoring-visits/{visit_id}/letters", {
        params: { path: { visit_id: visitId } },
      })
    );
  },

  /** Gets a generated visit letter by type (GET /api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}). */
  getMonitoringVisitLetterByType(
    visitId: string,
    letterType: string
  ): Promise<GeneratedLetterResponse> {
    return unwrapResponse(
      client.GET(
        "/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}",
        {
          params: {
            path: { visit_id: visitId, letter_type: letterType },
          },
        }
      )
    );
  },

  /** Renders a CTMS site visit letter preview via Liquid template (POST /api/v1/ctms/letters/render). */
  renderVisitLetter(
    templateName: string,
    payload: SiteVisitLetterModel
  ): Promise<string> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/letters/render", {
        params: { query: { templateName } },
        body: payload,
        parseAs: "text",
      })
    );
  },

  /** Compiles a GxP PDF visit letter with Part 11 signature and eTMF filing (POST /api/v1/ctms/letters/compile). */
  compileVisitLetter(
    templateName: string,
    payload: SiteVisitLetterModel,
    options: SignedMutationOptions = {}
  ): Promise<VisitLetterCompilationResult> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/letters/compile", {
        params: { query: { templateName } },
        body: payload,
        headers: buildAuditHeaders({
          changeReason:
            options.changeReason ?? payload.reasonForChange ?? undefined,
          sigToken: options.sigToken,
        }),
      })
    );
  },

  /** Downloads a compiled GxP PDF visit letter with bearer authorization (GET /api/v1/ctms/letters/{letterId}/pdf). */
  downloadVisitLetterPdf(letterId: string): Promise<Blob> {
    return unwrapResponse<Blob>(
      client.GET("/api/v1/ctms/letters/{letterId}/pdf", {
        params: { path: { letterId } },
        parseAs: "blob",
      }) as unknown as Promise<{
        data?: Blob;
        error?: unknown;
        response: Response;
      }>
    );
  },

  /** Fetches the active and historical Delegation of Authority log for a site (GET /api/v1/ctms/doa/sites/{site_id}/log). */
  getSiteDoaLog(siteId: string): Promise<DoaLogResponse> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/doa/sites/{site_id}/log", {
        params: { path: { site_id: siteId } },
      })
    );
  },

  /** Assigns site trial task delegation requiring PI sign-off (POST /api/v1/ctms/doa/delegate). */
  delegateSiteTasks(
    payload: DelegationTaskRequest
  ): Promise<DelegationResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/delegate", {
        body: payload,
        headers: buildAuditHeaders({
          changeReason: payload.reason_for_change ?? undefined,
        }),
      })
    );
  },

  /** Revokes or ends a delegated trial duty (POST /api/v1/ctms/doa/revoke). */
  revokeSiteTasks(
    payload: RevokeDelegationRequest
  ): Promise<DelegationResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/revoke", {
        body: payload,
        headers: buildAuditHeaders({
          changeReason: payload.reason_for_change ?? undefined,
        }),
      })
    );
  },

  /** Endorses Delegation of Authority task assignment with PI eSignature (POST /api/v1/ctms/doa/sign-off). */
  signOffDelegation(
    payload: DoaSignOffRequest,
    sigToken?: string
  ): Promise<DelegationResponse> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/sign-off", {
        body: payload,
        headers: buildAuditHeaders({
          changeReason: payload.reason_for_change ?? undefined,
          sigToken,
        }),
      })
    );
  },

  /** Exports 21 CFR Part 11 signed DOA PDF log with bearer authorization (GET /api/v1/ctms/doa/sites/{site_id}/export-pdf). */
  exportSiteDoaPdf(siteId: string): Promise<Blob> {
    return unwrapResponse<Blob>(
      client.GET("/api/v1/ctms/doa/sites/{site_id}/export-pdf", {
        params: { path: { site_id: siteId } },
        parseAs: "blob",
      }) as unknown as Promise<{
        data?: Blob;
        error?: unknown;
        response: Response;
      }>
    );
  },
};
