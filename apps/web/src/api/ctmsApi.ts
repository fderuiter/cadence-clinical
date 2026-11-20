import { getBearerToken } from "./authorization.js";
import { encodeChangeReason } from "./changeReason";
import {
  ApiError,
  createOpenApiClient,
  getBaseUrl,
  unwrapResponse,
} from "./client";
import type { components, paths } from "./generated/ctms";

/**
 * Typed client for the .NET CTMS service, generated from docs/openapi/ctms_openapi.json (ADR-2610).
 *
 * Every CTMS call in the web app goes through this client: startup greenlight readiness and certification,
 * site milestones, monitoring visits and letters, and Delegation of Authority (DoA) logs.
 */
type Schemas = components["schemas"];

export type SiteGreenlightGate = Schemas["Ctms_SiteGreenlightGateResponseDto"];
export type BatchCertifyRequest = Schemas["Ctms_BatchCertifyRequestDto"];
export type BatchCertifyResponse = Schemas["Ctms_BatchCertifyResponseDto"];
export type CertifiedSiteItem = Schemas["Ctms_CertifiedSiteItemDto"];
export type SiteMilestone = Schemas["Ctms_SiteMilestoneResponseDto"];
export type SiteMilestoneCreate = Schemas["Ctms_SiteMilestoneCreateDto"];
export type SiteMilestoneUpdate = Schemas["Ctms_SiteMilestoneUpdateDto"];
export type MonitoringVisit = Schemas["Ctms_MonitoringVisitResponseDto"];
export type MonitoringVisitCreate = Schemas["Ctms_MonitoringVisitCreateDto"];
export type MonitoringVisitComplete =
  Schemas["Ctms_MonitoringVisitCompleteDto"];
export type MonitoringFindingCreate = Schemas["Ctms_FindingCreateDto"];
export type GeneratedVisitLetter = Schemas["Ctms_GeneratedLetterResponseDto"];
export type DoaLog = Schemas["Ctms_DoaLogResponseDto"];
export type DelegationRecord = Schemas["Ctms_DelegationResponseDto"];
export type DelegationTaskRequest = Schemas["Ctms_DelegationTaskRequest"];
export type RevokeDelegationRequest = Schemas["Ctms_RevokeDelegationRequest"];
export type DoaSignOffRequest = Schemas["Ctms_DoaSignOffRequest"];
export type DoaAuditHistoryItem = Schemas["Ctms_DoaAuditHistoryItemDto"];

export interface ListSiteMilestonesQuery {
  studyId?: string;
  siteId?: string;
}

export interface ListMonitoringVisitsQuery {
  studyId?: string;
  siteId?: string;
  craId?: string;
  status?: string;
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
    countryCode?: string
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
        headers: { "X-Change-Reason": encodedReason },
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

  /** Batch-certifies selected 100% compliant sites in a country with a single Part 11 justification. */
  batchCertifyCountrySites(
    payload: BatchCertifyRequest,
    changeReason: string
  ): Promise<BatchCertifyResponse> {
    const encodedReason = encodeChangeReason(changeReason);
    return unwrapResponse(
      client.POST("/api/v1/ctms/startup/matrix/batch-certify", {
        params: {
          header: { "X-Change-Reason": encodedReason },
        },
        body: payload,
        headers: { "X-Change-Reason": encodedReason },
      })
    );
  },

  /** Lists site milestones filtered by study and/or site within caller scope. */
  listSiteMilestones(query: ListSiteMilestonesQuery): Promise<SiteMilestone[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/site-milestones", {
        params: {
          query: {
            study_id: query.studyId || undefined,
            site_id: query.siteId || undefined,
          },
        },
      })
    );
  },

  /** Creates a site milestone with a required Part 11 reason for change. */
  createSiteMilestone(
    payload: SiteMilestoneCreate,
    changeReason: string
  ): Promise<SiteMilestone> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/site-milestones", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? changeReason,
        },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Updates a site milestone status or dates with a required Part 11 reason for change. */
  updateSiteMilestone(
    milestoneId: string,
    payload: SiteMilestoneUpdate,
    changeReason: string
  ): Promise<SiteMilestone> {
    return unwrapResponse(
      client.PUT("/api/v1/ctms/site-milestones/{milestone_id}", {
        params: { path: { milestone_id: milestoneId } },
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? changeReason,
        },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Lists monitoring visits filtered by study, site, CRA, or status within caller scope. */
  listMonitoringVisits(
    query: ListMonitoringVisitsQuery
  ): Promise<MonitoringVisit[]> {
    return unwrapResponse(
      client.GET("/api/v1/ctms/monitoring-visits", {
        params: {
          query: {
            study_id: query.studyId || undefined,
            site_id: query.siteId || undefined,
            cra_id: query.craId || undefined,
            status: query.status || undefined,
          },
        },
      })
    );
  },

  /** Schedules a new monitoring visit. */
  scheduleMonitoringVisit(
    payload: MonitoringVisitCreate,
    changeReason: string
  ): Promise<MonitoringVisit> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? changeReason,
        },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Completes a scheduled monitoring visit with actual date and clinical findings. */
  completeMonitoringVisit(
    visitId: string,
    payload: MonitoringVisitComplete,
    changeReason: string
  ): Promise<MonitoringVisit> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits/{visit_id}/complete", {
        params: { path: { visit_id: visitId } },
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? changeReason,
        },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Records supervisory Part 11 sign-off on a completed monitoring visit. */
  signOffMonitoringVisit(
    visitId: string,
    changeReason: string
  ): Promise<MonitoringVisit> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/monitoring-visits/{visit_id}/sign-off", {
        params: { path: { visit_id: visitId } },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
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
    changeReason: string
  ): Promise<DelegationRecord> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/delegate", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? changeReason,
        },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Revokes an active delegated trial duty with reason for change. */
  revokeSiteTasks(
    payload: RevokeDelegationRequest,
    changeReason: string
  ): Promise<DelegationRecord> {
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/revoke", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? changeReason,
        },
        headers: { "X-Change-Reason": encodeChangeReason(changeReason) },
      })
    );
  },

  /** Endorses a Delegation of Authority record with Principal Investigator sign-off. */
  signOffDelegation(
    payload: DoaSignOffRequest,
    changeReason: string,
    signatureToken?: string
  ): Promise<DelegationRecord> {
    const headers: Record<string, string> = {
      "X-Change-Reason": encodeChangeReason(changeReason),
    };
    if (signatureToken) {
      headers["X-Sig-Token"] = signatureToken;
    }
    return unwrapResponse(
      client.POST("/api/v1/ctms/doa/sign-off", {
        body: {
          ...payload,
          reason_for_change: payload.reason_for_change ?? changeReason,
        },
        headers,
      })
    );
  },

  /** Exports the 21 CFR Part 11 signed Delegation of Authority PDF log for a site. */
  async exportSiteDoaPdf(siteId: string): Promise<Blob> {
    const token = await getBearerToken();
    const baseUrl = getBaseUrl().replace(/\/+$/, "");
    const headers: Record<string, string> = {};
    if (token) {
      headers.Authorization = `Bearer ${token}`;
    }
    const response = await globalThis.fetch(
      `${baseUrl}/api/v1/ctms/doa/sites/${encodeURIComponent(siteId)}/export-pdf`,
      { method: "GET", headers }
    );
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
  },
};
