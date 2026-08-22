import { apiClient } from "./apiClient";

/**
 * Service module for the eConsent microservice.
 * Interfaces with templates, clauses, comprehension checks, and translations.
 */
export const econsentService = {
  // --- Clauses ---
  createClause(body, options = {}) {
    return apiClient.post("/api/v1/econsent/clauses", body, options);
  },

  updateClause(clauseId, body, options = {}) {
    return apiClient.put(`/api/v1/econsent/clauses/${clauseId}`, body, options);
  },

  listClauses(params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.clause_id) query.append("clause_id", params.clause_id);
    if (params.all_versions) query.append("all_versions", params.all_versions);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/clauses?${queryString}`
      : "/api/v1/econsent/clauses";
    return apiClient.get(path, options);
  },

  getClause(clauseId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/clauses/${clauseId}?${queryString}`
      : `/api/v1/econsent/clauses/${clauseId}`;
    return apiClient.get(path, options);
  },

  // --- Templates ---
  createTemplate(body, options = {}) {
    return apiClient.post("/api/v1/econsent/templates", body, options);
  },

  updateTemplate(templateId, body, options = {}) {
    return apiClient.put(
      `/api/v1/econsent/templates/${templateId}`,
      body,
      options
    );
  },

  listTemplates(params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.template_id) query.append("template_id", params.template_id);
    if (params.all_versions) query.append("all_versions", params.all_versions);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/templates?${queryString}`
      : "/api/v1/econsent/templates";
    return apiClient.get(path, options);
  },

  getTemplate(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/templates/${templateId}?${queryString}`
      : `/api/v1/econsent/templates/${templateId}`;
    return apiClient.get(path, options);
  },

  composeTemplate(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/templates/${templateId}/compose?${queryString}`
      : `/api/v1/econsent/templates/${templateId}/compose`;
    return apiClient.get(path, options);
  },

  publishTemplate(templateId, options = {}) {
    return apiClient.post(
      `/api/v1/econsent/templates/${templateId}/publish`,
      {},
      options
    );
  },

  // --- Comprehension Checks ---
  defineComprehensionCheck(templateId, versionIndex, body, options = {}) {
    return apiClient.post(
      `/api/v1/econsent/templates/${templateId}/versions/${versionIndex}/comprehension-checks`,
      body,
      options
    );
  },

  getComprehensionCheck(templateId, versionIndex, options = {}) {
    return apiClient.get(
      `/api/v1/econsent/templates/${templateId}/versions/${versionIndex}/comprehension-checks`,
      options
    );
  },

  // --- Translations ---
  createTranslation(body, options = {}) {
    return apiClient.post("/api/v1/econsent/translations", body, options);
  },

  updateTranslation(translationId, body, options = {}) {
    return apiClient.put(
      `/api/v1/econsent/translations/${translationId}`,
      body,
      options
    );
  },

  listTranslations(params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.source_id) query.append("source_id", params.source_id);
    if (params.source_type) query.append("source_type", params.source_type);
    if (params.language_code)
      query.append("language_code", params.language_code);
    if (params.status) query.append("status", params.status);
    if (params.all_versions) query.append("all_versions", params.all_versions);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/translations?${queryString}`
      : "/api/v1/econsent/translations";
    return apiClient.get(path, options);
  },

  getTranslation(translationId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/translations/${translationId}?${queryString}`
      : `/api/v1/econsent/translations/${translationId}`;
    return apiClient.get(path, options);
  },

  transitionTranslation(translationId, body, options = {}) {
    return apiClient.post(
      `/api/v1/econsent/translations/${translationId}/transition`,
      body,
      options
    );
  },

  // --- Approved Composed Content (Participant) ---
  getApprovedContent(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.language_code)
      query.append("language_code", params.language_code);
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/templates/${templateId}/approved-content?${queryString}`
      : `/api/v1/econsent/templates/${templateId}/approved-content`;
    return apiClient.get(path, options);
  },

  // --- Comprehension Evaluation & Signature Capture ---
  submitAnswers(templateId, versionIndex, body, options = {}) {
    return apiClient.post(
      `/api/v1/econsent/templates/${templateId}/versions/${versionIndex}/submit-answers`,
      body,
      options
    );
  },

  signTemplate(templateId, versionIndex, body, options = {}) {
    return apiClient.post(
      `/api/v1/econsent/templates/${templateId}/versions/${versionIndex}/sign`,
      body,
      options
    );
  },

  captureConsent(templateId, versionIndex, body, options = {}) {
    return apiClient.post(
      `/api/v1/econsent/templates/${templateId}/versions/${versionIndex}/capture-consent`,
      body,
      options
    );
  },

  getSubjectConsentStatus(subjectPseudonym, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    const queryString = query.toString();
    const path = queryString
      ? `/api/v1/econsent/subjects/${subjectPseudonym}/consent-status?${queryString}`
      : `/api/v1/econsent/subjects/${subjectPseudonym}/consent-status`;
    return apiClient.get(path, options);
  },

  diffTemplateVersions(templateId, baseVersion, targetVersion, options = {}) {
    return apiClient.get(
      `/api/v1/econsent/templates/${templateId}/diff/${baseVersion}/${targetVersion}`,
      options
    );
  },

  // --- Readability & Jargon Harmonization ---
  analyzeReadability(body, options = {}) {
    return apiClient.post("/api/v1/econsent/readability/analyze", body, options);
  },

  harmonizeReadability(body, options = {}) {
    return apiClient.post("/api/v1/econsent/readability/harmonize", body, options);
  },

  harmonizeClause(clauseId, body, options = {}) {
    return apiClient.post(
      `/api/v1/econsent/clauses/${clauseId}/harmonize`,
      body,
      options
    );
  },
};

