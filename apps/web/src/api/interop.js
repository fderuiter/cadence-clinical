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

  /**
   * Registers a new hospital EHR FHIR endpoint with SMART Backend Services credentials.
   */
  registerEhrEndpoint(body, options = {}) {
    return apiClient.post(`/api/v1/interop/esource/endpoints`, body, options);
  },

  /**
   * Lists configured hospital EHR endpoints.
   */
  listEhrEndpoints(activeOnly = true, options = {}) {
    return apiClient.get(`/api/v1/interop/esource/endpoints?active_only=${activeOnly}`, options);
  },

  /**
   * Generates a signed SMART Backend Services client assertion JWT.
   */
  generateClientAssertion(body, options = {}) {
    return apiClient.post(`/api/v1/interop/esource/smart/assertion`, body, options);
  },

  /**
   * Verifies an asymmetric client assertion JWT.
   */
  verifyClientAssertion(body, options = {}) {
    return apiClient.post(`/api/v1/interop/esource/smart/verify`, body, options);
  },

  /**
   * Triggers automated SMART on FHIR polling against a hospital EHR endpoint.
   */
  pollHospitalEhr(body, options = {}) {
    return apiClient.post(`/api/v1/interop/esource/poll`, body, options);
  },

  /**
   * Directly ingests and normalizes a FHIR Bundle into the candidate reconciliation queue.
   */
  ingestFhirBundle(body, options = {}) {
    return apiClient.post(`/api/v1/interop/esource/ingest`, body, options);
  },

  /**
   * Lists candidate reconciliation records with study, subject, domain, and status filters.
   */
  listEhrCandidates(params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.studyId) query.append("study_id", params.studyId);
    if (params.subjectId) query.append("subject_id", params.subjectId);
    if (params.siteId) query.append("site_id", params.siteId);
    if (params.domain) query.append("domain", params.domain);
    if (params.status) query.append("status", params.status);
    if (params.skip !== undefined) query.append("skip", String(params.skip));
    if (params.limit !== undefined) query.append("limit", String(params.limit));
    const qs = query.toString();
    return apiClient.get(`/api/v1/interop/esource/candidates${qs ? `?${qs}` : ""}`, options);
  },

  /**
   * Retrieves a specific candidate reconciliation record.
   */
  getEhrCandidate(candidateId, options = {}) {
    return apiClient.get(`/api/v1/interop/esource/candidates/${encodeURIComponent(candidateId)}`, options);
  },

  /**
   * 1-Click 'Accept & Commit' candidate EHR data into eCRF with Part 11 audit attribution.
   */
  acceptEhrCandidate(body, options = {}) {
    return apiClient.post(`/api/v1/interop/esource/reconcile/accept`, body, options);
  },

  /**
   * 1-Click 'Reject with Reason' to discard candidate EHR data with mandatory justification.
   */
  rejectEhrCandidate(body, options = {}) {
    return apiClient.post(`/api/v1/interop/esource/reconcile/reject`, body, options);
  },

  /**
   * 1-Click 'Raise Clinical Query' to open a formal discrepancy query for an EHR-to-eCRF mismatch.
   */
  raiseEhrClinicalQuery(body, options = {}) {
    return apiClient.post(`/api/v1/interop/esource/reconcile/query`, body, options);
  },

  /**
   * Retrieves complete immutable FHIR provenance and Part 11 audit history for a candidate record.
   */
  getEhrProvenance(candidateId, options = {}) {
    return apiClient.get(`/api/v1/interop/esource/provenance/${encodeURIComponent(candidateId)}`, options);
  },

  // =========================================================================
  // Decentralized Clinical Trials (DCT) & Telehealth Methods
  // =========================================================================

  scheduleTelehealthSession(body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/telehealth/sessions`, body, options);
  },

  listTelehealthSessions(params = {}, options = {}) {
    const qs = new URLSearchParams();
    if (params.studyId) qs.append("study_id", params.studyId);
    if (params.subjectId) qs.append("subject_id", params.subjectId);
    return apiClient.get(`/api/v1/interop/dct/telehealth/sessions${qs.toString() ? `?${qs.toString()}` : ""}`, options);
  },

  getTelehealthSession(sessionId, options = {}) {
    return apiClient.get(`/api/v1/interop/dct/telehealth/sessions/${encodeURIComponent(sessionId)}`, options);
  },

  generateRoomToken(sessionId, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/telehealth/sessions/${encodeURIComponent(sessionId)}/token`, body, options);
  },

  sendWebRTCSignaling(sessionId, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/telehealth/sessions/${encodeURIComponent(sessionId)}/signaling`, body, options);
  },

  syncCoViewState(sessionId, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/telehealth/sessions/${encodeURIComponent(sessionId)}/co-view`, body, options);
  },

  archiveTelehealthRecording(sessionId, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/telehealth/sessions/${encodeURIComponent(sessionId)}/archive-recording`, body, options);
  },

  submitPediatricAssent(body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/econsent/pediatric-assent`, body, options);
  },

  listPediatricAssents(params = {}, options = {}) {
    const qs = new URLSearchParams();
    if (params.studyId) qs.append("study_id", params.studyId);
    if (params.subjectId) qs.append("subject_id", params.subjectId);
    return apiClient.get(`/api/v1/interop/dct/econsent/pediatric-assent${qs.toString() ? `?${qs.toString()}` : ""}`, options);
  },

  verifyPediatricAssent(assentId, options = {}) {
    return apiClient.get(`/api/v1/interop/dct/econsent/pediatric-assent/${encodeURIComponent(assentId)}/verify`, options);
  },

  createDtpShipment(body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/dtp/shipments`, body, options);
  },

  listDtpShipments(params = {}, options = {}) {
    const qs = new URLSearchParams();
    if (params.studyId) qs.append("study_id", params.studyId);
    if (params.siteId) qs.append("site_id", params.siteId);
    if (params.subjectPseudonymId) qs.append("subject_pseudonym_id", params.subjectPseudonymId);
    return apiClient.get(`/api/v1/interop/dct/dtp/shipments${qs.toString() ? `?${qs.toString()}` : ""}`, options);
  },

  getDtpShipment(trackingNumber, options = {}) {
    return apiClient.get(`/api/v1/interop/dct/dtp/shipments/${encodeURIComponent(trackingNumber)}`, options);
  },

  ingestDtpTemperature(trackingNumber, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/dtp/shipments/${encodeURIComponent(trackingNumber)}/temperature`, body, options);
  },

  recordDtpCustodyHandoff(trackingNumber, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/dtp/shipments/${encodeURIComponent(trackingNumber)}/handoff`, body, options);
  },

  confirmDtpDelivery(trackingNumber, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/dtp/shipments/${encodeURIComponent(trackingNumber)}/delivery`, body, options);
  },

  scheduleInHomeVisit(body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/home-health/visits`, body, options);
  },

  listInHomeVisits(params = {}, options = {}) {
    const qs = new URLSearchParams();
    if (params.studyId) qs.append("study_id", params.studyId);
    if (params.siteId) qs.append("site_id", params.siteId);
    if (params.nurseId) qs.append("nurse_id", params.nurseId);
    if (params.subjectId) qs.append("subject_id", params.subjectId);
    return apiClient.get(`/api/v1/interop/dct/home-health/visits${qs.toString() ? `?${qs.toString()}` : ""}`, options);
  },

  getInHomeVisit(visitId, options = {}) {
    return apiClient.get(`/api/v1/interop/dct/home-health/visits/${encodeURIComponent(visitId)}`, options);
  },

  updateInHomeVisitStatus(visitId, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/home-health/visits/${encodeURIComponent(visitId)}/status`, body, options);
  },

  syncMobileECRF(visitId, body, options = {}) {
    return apiClient.post(`/api/v1/interop/dct/home-health/visits/${encodeURIComponent(visitId)}/sync-ecrf`, body, options);
  },

  getDctMetrics(studyId, options = {}) {
    return apiClient.get(`/api/v1/interop/dct/metrics/${encodeURIComponent(studyId)}`, options);
  },
};

