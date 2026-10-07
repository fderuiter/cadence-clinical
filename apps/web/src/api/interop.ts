import { openApiClient, unwrapResponse } from "./client";
import type { paths } from "./types";

export interface CandidateListParams {
  studyId?: string;
  subjectId?: string;
  siteId?: string;
  domain?: string;
  status?: string;
  skip?: number;
  limit?: number;
}

export interface TelehealthSessionParams {
  studyId?: string;
  subjectId?: string;
}

export interface PediatricAssentParams {
  studyId?: string;
  subjectId?: string;
}

export interface DtpShipmentParams {
  studyId?: string;
  siteId?: string;
  subjectPseudonymId?: string;
}

export interface InHomeVisitParams {
  studyId?: string;
  siteId?: string;
  nurseId?: string;
  subjectId?: string;
}

export const interopService = {
  submitEpro(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/epro/submit", {
        body: body as paths["/api/v1/interop/epro/submit"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  syncEpro(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/epro/sync", {
        body: body as paths["/api/v1/interop/epro/sync"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  getInstruments(subjectId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/subjects/{subject_id}/instruments", {
        params: { path: { subject_id: subjectId } },
        ...options,
      })
    );
  },

  registerSensorDevice(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sensors/devices", {
        body: body as paths["/api/v1/interop/sensors/devices"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listSensorDevices(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sensors/devices", {
        params: { query: { study_id: studyId } },
        ...options,
      })
    );
  },

  ingestSensorTelemetry(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/sensors/ingest", {
        body: body as paths["/api/v1/interop/sensors/ingest"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listDailySummaries(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sensors/daily-summaries", {
        params: { query: { study_id: studyId } },
        ...options,
      })
    );
  },

  listSafetyAlerts(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sensors/safety-alerts", {
        params: { query: { study_id: studyId } },
        ...options,
      })
    );
  },

  acknowledgeSafetyAlert(
    alertId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge",
        {
          params: { path: { alert_id: alertId } },
          body: body as paths["/api/v1/interop/sensors/safety-alerts/{alert_id}/acknowledge"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  getSensorMetrics(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/sensors/metrics/{study_id}", {
        params: { path: { study_id: studyId } },
        ...options,
      })
    );
  },

  registerEhrEndpoint(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/endpoints", {
        body: body as paths["/api/v1/interop/esource/endpoints"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listEhrEndpoints(activeOnly = true, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/esource/endpoints", {
        params: { query: { active_only: activeOnly } },
        ...options,
      })
    );
  },

  generateClientAssertion(
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/smart/assertion", {
        body: body as paths["/api/v1/interop/esource/smart/assertion"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  verifyClientAssertion(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/smart/verify", {
        body: body as paths["/api/v1/interop/esource/smart/verify"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  pollHospitalEhr(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/poll", {
        body: body as paths["/api/v1/interop/esource/poll"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  ingestFhirBundle(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/ingest", {
        body: body as paths["/api/v1/interop/esource/ingest"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listEhrCandidates(
    params: CandidateListParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/esource/candidates", {
        params: {
          query: {
            study_id: params.studyId,
            subject_id: params.subjectId,
            site_id: params.siteId,
            domain: params.domain,
            status: params.status,
            skip: params.skip,
            limit: params.limit,
          },
        },
        ...options,
      })
    );
  },

  getEhrCandidate(candidateId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/esource/candidates/{candidate_id}", {
        params: { path: { candidate_id: candidateId } },
        ...options,
      })
    );
  },

  acceptEhrCandidate(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/reconcile/accept", {
        body: body as paths["/api/v1/interop/esource/reconcile/accept"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  batchAcceptEhrCandidates(
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/reconcile/batch-accept", {
        body: body as paths["/api/v1/interop/esource/reconcile/batch-accept"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  rejectEhrCandidate(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/reconcile/reject", {
        body: body as paths["/api/v1/interop/esource/reconcile/reject"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  batchRejectEhrCandidates(
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/reconcile/batch-reject", {
        body: body as paths["/api/v1/interop/esource/reconcile/batch-reject"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  raiseEhrClinicalQuery(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/esource/reconcile/query", {
        body: body as paths["/api/v1/interop/esource/reconcile/query"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  getEhrProvenance(candidateId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/esource/provenance/{candidate_id}", {
        params: { path: { candidate_id: candidateId } },
        ...options,
      })
    );
  },

  // Telehealth & DCT Methods
  scheduleTelehealthSession(
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/dct/telehealth/sessions", {
        body: body as paths["/api/v1/interop/dct/telehealth/sessions"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listTelehealthSessions(
    params: TelehealthSessionParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/telehealth/sessions", {
        params: {
          query: {
            study_id: params.studyId,
            subject_id: params.subjectId,
          },
        },
        ...options,
      })
    );
  },

  getTelehealthSession(
    sessionId: string,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/interop/dct/telehealth/sessions/{session_id}",
        {
          params: { path: { session_id: sessionId } },
          ...options,
        }
      )
    );
  },

  generateRoomToken(
    sessionId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/telehealth/sessions/{session_id}/token",
        {
          params: { path: { session_id: sessionId } },
          body: body as paths["/api/v1/interop/dct/telehealth/sessions/{session_id}/token"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  sendWebRTCSignaling(
    sessionId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/telehealth/sessions/{session_id}/signaling",
        {
          params: { path: { session_id: sessionId } },
          body: body as paths["/api/v1/interop/dct/telehealth/sessions/{session_id}/signaling"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  syncCoViewState(
    sessionId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/telehealth/sessions/{session_id}/co-view",
        {
          params: { path: { session_id: sessionId } },
          body: body as paths["/api/v1/interop/dct/telehealth/sessions/{session_id}/co-view"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  archiveTelehealthRecording(
    sessionId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/telehealth/sessions/{session_id}/archive-recording",
        {
          params: { path: { session_id: sessionId } },
          body: body as paths["/api/v1/interop/dct/telehealth/sessions/{session_id}/archive-recording"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  submitPediatricAssent(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/dct/econsent/pediatric-assent", {
        body: body as paths["/api/v1/interop/dct/econsent/pediatric-assent"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listPediatricAssents(
    params: PediatricAssentParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/econsent/pediatric-assent", {
        params: {
          query: {
            study_id: params.studyId,
            subject_id: params.subjectId,
          },
        },
        ...options,
      })
    );
  },

  verifyPediatricAssent(
    assentId: string,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET(
        "/api/v1/interop/dct/econsent/pediatric-assent/{assent_id}/verify",
        {
          params: { path: { assent_id: assentId } },
          ...options,
        }
      )
    );
  },

  createDtpShipment(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/dct/dtp/shipments", {
        body: body as paths["/api/v1/interop/dct/dtp/shipments"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listDtpShipments(
    params: DtpShipmentParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/dtp/shipments", {
        params: {
          query: {
            study_id: params.studyId,
            site_id: params.siteId,
            subject_pseudonym_id: params.subjectPseudonymId,
          },
        },
        ...options,
      })
    );
  },

  getDtpShipment(
    trackingNumber: string,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/dtp/shipments/{tracking_number}", {
        params: { path: { tracking_number: trackingNumber } },
        ...options,
      })
    );
  },

  ingestDtpTemperature(
    trackingNumber: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/dtp/shipments/{tracking_number}/temperature",
        {
          params: { path: { tracking_number: trackingNumber } },
          body: body as paths["/api/v1/interop/dct/dtp/shipments/{tracking_number}/temperature"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  recordDtpCustodyHandoff(
    trackingNumber: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/dtp/shipments/{tracking_number}/handoff",
        {
          params: { path: { tracking_number: trackingNumber } },
          body: body as paths["/api/v1/interop/dct/dtp/shipments/{tracking_number}/handoff"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  confirmDtpDelivery(
    trackingNumber: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/dtp/shipments/{tracking_number}/delivery",
        {
          params: { path: { tracking_number: trackingNumber } },
          body: body as paths["/api/v1/interop/dct/dtp/shipments/{tracking_number}/delivery"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  scheduleInHomeVisit(body: unknown, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.POST("/api/v1/interop/dct/home-health/visits", {
        body: body as paths["/api/v1/interop/dct/home-health/visits"]["post"]["requestBody"]["content"]["application/json"],
        ...options,
      })
    );
  },

  listInHomeVisits(
    params: InHomeVisitParams = {},
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/home-health/visits", {
        params: {
          query: {
            study_id: params.studyId,
            site_id: params.siteId,
            nurse_id: params.nurseId,
            subject_id: params.subjectId,
          },
        },
        ...options,
      })
    );
  },

  getInHomeVisit(visitId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/home-health/visits/{visit_id}", {
        params: { path: { visit_id: visitId } },
        ...options,
      })
    );
  },

  updateInHomeVisitStatus(
    visitId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/home-health/visits/{visit_id}/status",
        {
          params: { path: { visit_id: visitId } },
          body: body as paths["/api/v1/interop/dct/home-health/visits/{visit_id}/status"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  syncMobileECRF(
    visitId: string,
    body: unknown,
    options: Record<string, unknown> = {}
  ) {
    return unwrapResponse(
      openApiClient.POST(
        "/api/v1/interop/dct/home-health/visits/{visit_id}/sync-ecrf",
        {
          params: { path: { visit_id: visitId } },
          body: body as paths["/api/v1/interop/dct/home-health/visits/{visit_id}/sync-ecrf"]["post"]["requestBody"]["content"]["application/json"],
          ...options,
        }
      )
    );
  },

  getDctMetrics(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/metrics/{study_id}", {
        params: { path: { study_id: studyId } },
        ...options,
      })
    );
  },
};
