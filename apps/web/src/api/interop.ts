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

  getDctMetrics(studyId: string, options: Record<string, unknown> = {}) {
    return unwrapResponse(
      openApiClient.GET("/api/v1/interop/dct/metrics/{study_id}", {
        params: { path: { study_id: studyId } },
        ...options,
      })
    );
  },
};
