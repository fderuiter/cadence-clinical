import { unwrapResponse } from "./client";
import {
  esourceApi,
  type CandidateListParams,
  type NormalizedEhrCandidate,
  type NormalizedEhrCandidateListResponse,
  type NormalizedEhrReconcileActionResponse,
  type NormalizedEhrReconcileBatchActionResponse,
} from "./interop/esource";
import { openApiClient } from "./legacyClient";
import type { paths } from "./types";

export { esourceApi };
export type {
  CandidateListParams,
  NormalizedEhrCandidate,
  NormalizedEhrCandidateListResponse,
  NormalizedEhrReconcileActionResponse,
  NormalizedEhrReconcileBatchActionResponse,
};

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

  ...esourceApi,

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
