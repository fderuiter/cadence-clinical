import {
  getBearerToken,
  refreshBearerTokenOnUnauthorized,
} from "./authorization.js";
import { encodeChangeReason } from "./changeReason";
import {
  ApiError,
  createOpenApiClient,
  getBaseUrl,
  unwrapResponse,
} from "./client";
import type { components, paths } from "./generated/ctms";

/**
 * Typed client for the .NET CTMS service, generated from docs/openapi/ctms_openapi.json (ADR-2610, ADR-2642, ADR-2643).
 *
 * Every CTMS call in the web app goes through this client: startup greenlight readiness and certification,
 * site milestones, monitoring visits and compiled signed letters, and Delegation of Authority (DoA) logs.
 */
type Schemas = components["schemas"];

export type SiteGreenlightGate = Schemas["Ctms_SiteGreenlightGateResponseDto"];
export type SiteGreenlightGateResponse = SiteGreenlightGate;
export type BatchCertifyRequest = Schemas["Ctms_BatchCertifyRequestDto"];
export type BatchCertifyResponse = Schemas["Ctms_BatchCertifyResponseDto"];
export type CertifiedSiteItem = Schemas["Ctms_CertifiedSiteItemDto"];

export type SiteMilestone = Schemas["Ctms_SiteMilestoneResponseDto"];
export type SiteMilestoneResponse = SiteMilestone;
export type SiteMilestoneCreate = Schemas["Ctms_SiteMilestoneCreateDto"];
export type SiteMilestoneUpdate = Schemas["Ctms_SiteMilestoneUpdateDto"];

export type MonitoringVisit = Schemas["Ctms_MonitoringVisitResponseDto"];
export type MonitoringVisitResponse = MonitoringVisit;
export type MonitoringVisitCreate = Schemas["Ctms_MonitoringVisitCreateDto"];
export type MonitoringVisitComplete =
  Schemas["Ctms_MonitoringVisitCompleteDto"];
export type MonitoringFindingCreate = Schemas["Ctms_FindingCreateDto"];
export type FindingCreate = MonitoringFindingCreate;
export type GeneratedVisitLetter = Schemas["Ctms_GeneratedLetterResponseDto"];
export type GeneratedLetterResponse = GeneratedVisitLetter;

export type SiteVisitFindingModel = Schemas["Ctms_SiteVisitFindingModel"];
export type SiteVisitLetterModel = Schemas["Ctms_SiteVisitLetterModel"];
export type VisitLetterCompilationResult =
  Schemas["Ctms_VisitLetterCompilationResultDto"];

export type DoaLog = Schemas["Ctms_DoaLogResponseDto"];
export type DoaLogResponse = DoaLog;
export type DelegationRecord = Schemas["Ctms_DelegationResponseDto"];
export type DelegationResponse = DelegationRecord;
export type DelegationTaskRequest = Schemas["Ctms_DelegationTaskRequest"];
export type RevokeDelegationRequest = Schemas["Ctms_RevokeDelegationRequest"];
export type DoaSignOffRequest = Schemas["Ctms_DoaSignOffRequest"];
export type DoaAuditHistoryItem = Schemas["Ctms_DoaAuditHistoryItemDto"];

export interface ListSiteMilestonesQuery {
  studyId?: string;
  siteId?: string;
  study_id?: string;
  site_id?: string;
}

export interface ListMonitoringVisitsQuery {
  studyId?: string;
  siteId?: string;
  craId?: string;
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

async function fetchAuthenticatedPdf(pathSuffix: string): Promise<Blob> {
  const token = await getBearerToken();
  const baseUrl = getBaseUrl().replace(/\/+$/, "");
  const url = `${baseUrl}${pathSuffix}`;
  const headers: Record<string, string> = {};
  if (token) {
    headers.Authorization = `Bearer ${token}`;
  }
  let response = await globalThis.fetch(url, { method: "GET", headers });
  if (response.status === 401) {
    const errBody = await response
      .clone()
      .json()
      .catch(() => null);
    const freshToken = await refreshBearerTokenOnUnauthorized(url, errBody);
    if (freshToken) {
      response = await globalThis.fetch(url, {
        method: "GET",
        headers: { ...headers, Authorization: `Bearer ${freshToken}` },
      });
    }
  }
  if (!response.ok) {
    const errData = await response.json().catch(() => null);
    const detail =
      (errData as Record<string, unknown> | null)?.detail ||
      `Request failed with status ${response.status}`;
    throw new ApiError(
      String(detail),
      response.status,
      response.statusText,
      errData
    );
  }
  return response.blob();
}

const client = createOpenApiClient<paths>();

export const ctmsApi = {
  /** Evaluates 6-facet startup greenlight readiness for a single site. */
  evaluateSiteGreenlight(
    siteId: string,
    studyId: string,
    countryCode?: string
  ): Promise<SiteGreenlightGate> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/startup/sites/{siteId}/greenlight", {
        params: {
          path: { siteId },
          query: {
            study_id: studyId,
            country_code: countryCode || undefined,
          },
        },
      })
    );
  },

  /** Certifies startup greenlight readiness for a single compliant site with Part 11 change reason. */
  certifySiteGreenlight(
    siteId: string,
    studyId: string,
    changeReason: string,
    countryCode?: string,
    sigToken?: string
  ): Promise<SiteGreenlightGate> {
    const encodedReason = encodeChangeReason(changeReason);
    return unwrapResponse(
      client.POST("/api/v1/ctms/startup/sites/{siteId}/greenlight/certify", {
        params: {
          path: { siteId },
          query: {
            study_id: studyId,
            country_code: countryCode || undefined,
          },
          header: { "X-Change-Reason": encodedReason },
        },
        headers: buildAuditHeaders({ changeReason, sigToken }),
      })
    );
  },

  /** Lists 6-facet startup greenlight gates across caller-authorized sites in a country. */
  getCountryGreenlightMatrix(
    studyId: string,
    countryCode: string
  ): Promise<SiteGreenlightGate[]> {
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

  /** Alias for `getCountryGreenlightMatrix` used by country startup consoles. */
  getCountryMatrix(
    studyId: string,
    countryCode: string
  ): Promise<SiteGreenlightGate[]> {
    return this.getCountryGreenlightMatrix(studyId, countryCode);
  },

  /** Batch-certifies selected 100% compliant sites in a country with a single Part 11 justification. */
  batchCertifyCountrySites(
    payload: BatchCertifyRequest,
    changeReason: string,
    sigToken?: string
  ): Promise<BatchCertifyResponse> {
    const encodedReason = encodeChangeReason(changeReason);
    return unwrapResponse(
      client.POST("/api/v1/ctms/startup/matrix/batch-certify", {
        params: {
          header: { "X-Change-Reason": encodedReason },
        },
        body: payload,
        headers: buildAuditHeaders({ changeReason, sigToken }),
      })
    );
  },

  /** Lists site milestones filtered by study and/or site within caller scope. */
  listSiteMilestones(
    query: ListSiteMilestonesQuery = {}
  ): Promise<SiteMilestone[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/site-milestones", {
        params: {
          query: {
            study_id: (query.studyId ?? query.study_id) || undefined,
            site_id: (query.siteId ?? query.site_id) || undefined,
          },
        },
      })
    );
  },

  /** Creates a site milestone with a required Part 11 reason for change. */
  createSiteMilestone(
    payload: SiteMilestoneCreate,
    changeReason?: string
  ): Promise<SiteMilestone> {
    const reason = changeReason ?? payload.reason_for_change ?? undefined;
    return unwrapResponse(
      client.POST("/api/v1/ctms/site-milestones", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? reason,
        },
        headers: buildAuditHeaders({ changeReason: reason }),
      })
    );
  },

  /** Updates a site milestone status or dates with a required Part 11 reason for change. */
  updateSiteMilestone(
    milestoneId: string,
    payload: SiteMilestoneUpdate,
    changeReason?: string
  ): Promise<SiteMilestone> {
    const reason = changeReason ?? payload.reason_for_change ?? undefined;
    return unwrapResponse(
      client.PUT("/api/v1/ctms/site-milestones/{milestone_id}", {
        params: { path: { milestone_id: milestoneId } },
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? reason,
        },
        headers: buildAuditHeaders({ changeReason: reason }),
      })
    );
  },

  /** Lists monitoring visits filtered by study, site, CRA, or status within caller scope. */
  listMonitoringVisits(
    query: ListMonitoringVisitsQuery = {}
  ): Promise<MonitoringVisit[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/monitoring-visits", {
        params: {
          query: {
            study_id: (query.studyId ?? query.study_id) || undefined,
            site_id: (query.siteId ?? query.site_id) || undefined,
            cra_id: (query.craId ?? query.cra_id) || undefined,
            status: query.status || undefined,
          },
        },
      })
    );
  },

  /** Schedules a new monitoring visit. */
  scheduleMonitoringVisit(
    payload: MonitoringVisitCreate,
    changeReason?: string
  ): Promise<MonitoringVisit> {
    const reason = changeReason ?? payload.reason_for_change ?? undefined;
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? reason,
        },
        headers: buildAuditHeaders({ changeReason: reason }),
      })
    );
  },

  /** Completes a scheduled monitoring visit with actual date and clinical findings. */
  completeMonitoringVisit(
    visitId: string,
    payload: MonitoringVisitComplete,
    changeReason?: string
  ): Promise<MonitoringVisit> {
    const reason = changeReason ?? payload.reason_for_change ?? undefined;
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits/{visit_id}/complete", {
        params: { path: { visit_id: visitId } },
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? reason,
        },
        headers: buildAuditHeaders({ changeReason: reason }),
      })
    );
  },

  /** Records supervisory Part 11 sign-off on a completed monitoring visit. */
  signOffMonitoringVisit(
    visitId: string,
    changeReason: string,
    sigToken?: string
  ): Promise<MonitoringVisit> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits/{visit_id}/sign-off", {
        params: { path: { visit_id: visitId } },
        headers: buildAuditHeaders({ changeReason, sigToken }),
      })
    );
  },

  /** Lists generated confirmation and follow-up letters for a monitoring visit. */
  listMonitoringVisitLetters(visitId: string): Promise<GeneratedVisitLetter[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/monitoring-visits/{visit_id}/letters", {
        params: { path: { visit_id: visitId } },
      })
    );
  },

  /** Alias for `listMonitoringVisitLetters`. */
  getMonitoringVisitLetters(visitId: string): Promise<GeneratedVisitLetter[]> {
    return this.listMonitoringVisitLetters(visitId);
  },

  /** Retrieves a specific generated monitoring visit letter by type. */
  getMonitoringVisitLetterByType(
    visitId: string,
    letterType: string
  ): Promise<GeneratedVisitLetter> {
    return unwrapResponse(
      client.GET(
        "/api/v1/ctms/monitoring-visits/{visit_id}/letters/{letter_type}",
        {
          params: { path: { visit_id: visitId, letter_type: letterType } },
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
    return fetchAuthenticatedPdf(
      `/api/v1/ctms/letters/${encodeURIComponent(letterId)}/pdf`
    );
  },

  /** Fetches the active and historical Delegation of Authority (DoA) log for a site. */
  getSiteDoaLog(siteId: string): Promise<DoaLog> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/doa/sites/{site_id}/log", {
        params: { path: { site_id: siteId } },
      })
    );
  },

  /** Delegates trial duties to a site staff member pending Principal Investigator sign-off. */
  delegateSiteTasks(
    payload: DelegationTaskRequest,
    changeReason?: string
  ): Promise<DelegationRecord> {
    const reason = changeReason ?? payload.reason_for_change ?? undefined;
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/delegate", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? reason ?? null,
        },
        headers: buildAuditHeaders({ changeReason: reason }),
      })
    );
  },

  /** Revokes an active delegated trial duty with reason for change. */
  revokeSiteTasks(
    payload: RevokeDelegationRequest,
    changeReason?: string
  ): Promise<DelegationRecord> {
    const reason = changeReason ?? payload.reason_for_change ?? undefined;
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/revoke", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? reason ?? null,
        },
        headers: buildAuditHeaders({ changeReason: reason }),
      })
    );
  },

  /** Endorses a Delegation of Authority record with Principal Investigator sign-off. */
  signOffDelegation(
    payload: DoaSignOffRequest,
    changeReason?: string,
    signatureToken?: string
  ): Promise<DelegationRecord> {
    const reason = changeReason ?? payload.reason_for_change ?? undefined;
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/sign-off", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? reason ?? null,
        },
        headers: buildAuditHeaders({
          changeReason: reason,
          sigToken: signatureToken,
        }),
      })
    );
  },

  /** Exports the 21 CFR Part 11 signed Delegation of Authority PDF log for a site. */
  exportSiteDoaPdf(siteId: string): Promise<Blob> {
    return fetchAuthenticatedPdf(
      `/api/v1/ctms/doa/sites/${encodeURIComponent(siteId)}/export-pdf`
    );
  },
};
