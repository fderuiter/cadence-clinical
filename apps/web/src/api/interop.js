import { apiClient } from "./apiClient";

/**
 * Service module for the FHIR / ePRO Interoperability Gateway microservice.
 * Interfaces with ePRO submissions, offline sync reconciliation, and instrument metadata.
 */
export const interopService = {
  /**
   * Submits a completed ePRO/eCOA questionnaire.
   */
  submitEpro(body, options = {}) {
    return apiClient.post(`/api/v1/interop/epro/submit`, body, options);
  },

  /**
   * Synchronizes queued offline ePRO transactions with the server.
   */
  syncEpro(body, options = {}) {
    return apiClient.post(`/api/v1/interop/epro/sync`, body, options);
  },

  /**
   * Fetches active assigned instruments for a given subject.
   */
  getInstruments(subjectId, options = {}) {
    return apiClient.get(
      `/api/v1/interop/subjects/${subjectId}/instruments`,
      options
    );
  },

  /**
   * Registers and maps an approved medical IoT sensor device.
   */
  registerSensorDevice(body, options = {}) {
    return apiClient.post(`/api/v1/interop/sensors/devices`, body, options);
  },

  /**
   * Lists registered medical IoT sensor devices for a study.
   */
  listSensorDevices(studyId, options = {}) {
    return apiClient.get(`/api/v1/interop/sensors/devices?study_id=${encodeURIComponent(studyId)}`, options);
  },

  /**
   * Ingests continuous biometric IoT sensor telemetry stream.
   */
  ingestSensorTelemetry(body, options = {}) {
    return apiClient.post(`/api/v1/interop/sensors/ingest`, body, options);
  },

  /**
   * Queries calculated daily summary endpoints.
   */
  listDailySummaries(studyId, options = {}) {
    return apiClient.get(`/api/v1/interop/sensors/daily-summaries?study_id=${encodeURIComponent(studyId)}`, options);
  },

  /**
   * Queries automated safety threshold alerts.
   */
  listSafetyAlerts(studyId, options = {}) {
    return apiClient.get(`/api/v1/interop/sensors/safety-alerts?study_id=${encodeURIComponent(studyId)}`, options);
  },

  /**
   * Acknowledges a safety threshold alert with 21 CFR Part 11 sign-off.
   */
  acknowledgeSafetyAlert(alertId, body, options = {}) {
    return apiClient.post(`/api/v1/interop/sensors/safety-alerts/${encodeURIComponent(alertId)}/acknowledge`, body, options);
  },

  /**
   * Retrieves aggregate real-time IoT sensor metrics across a study.
   */
  getSensorMetrics(studyId, options = {}) {
    return apiClient.get(`/api/v1/interop/sensors/metrics/${encodeURIComponent(studyId)}`, options);
  },
};
