const fs = require('fs');
const path = require('path');

// This is the generator script for shared-api-client.
// It generates the pure TS/JS package contents of packages/shared-api-client
// and the auto-generated wrappers in apps/web/src/api.

const targetJsPath = '/app/packages/shared-api-client/index.js';
const targetDtsPath = '/app/packages/shared-api-client/index.d.ts';

const jsContent = `/**
 * CENTRALIZED API CLIENT PACKAGE (AUTO-GENERATED)
 */

let tokenProvider = null;
let changeReasonProvider = null;
let baseUrlProvider = null;
let signatureGenerator = null;

export const apiConfig = {
  setTokenProvider(fn) {
    tokenProvider = fn;
  },
  setChangeReasonProvider(fn) {
    changeReasonProvider = fn;
  },
  setBaseUrlProvider(fn) {
    baseUrlProvider = fn;
  },
  setSignatureGenerator(fn) {
    signatureGenerator = fn;
  },
  getSignatureGenerator() {
    return signatureGenerator;
  },
  getBaseUrl() {
    if (baseUrlProvider) return baseUrlProvider();
    if (typeof window !== "undefined" && window.location) {
      if (typeof import.meta !== "undefined" && import.meta.env?.VITE_API_BASE_URL) {
        return import.meta.env.VITE_API_BASE_URL;
      }
    }
    return "http://localhost:8000";
  }
};

export class ApiError extends Error {
  constructor(message, status = null, statusText = null, data = null) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.statusText = statusText;
    this.data = data;
  }
}

export class TerminologyNetworkError extends Error {
  constructor(message, status = null, statusText = null) {
    super(message);
    this.name = "TerminologyNetworkError";
    this.status = status;
    this.statusText = statusText;
  }
}

async function request(path, options = {}) {
  let token = null;
  if (tokenProvider) {
    try {
      token = tokenProvider();
    } catch (e) {
      // ignore
    }
  }

  const {
    method = "GET",
    headers = {},
    body,
    changeReason,
    ...customOptions
  } = options;

  const isFormData = typeof FormData !== "undefined" && body instanceof FormData;
  const requestHeaders = {
    ...headers,
  };

  if (!isFormData) {
    requestHeaders["Content-Type"] = "application/json";
  }

  if (token) {
    requestHeaders["Authorization"] = \`Bearer \${token}\`;
  }

  const upperMethod = method.toUpperCase();
  const isMutation = ["POST", "PUT", "DELETE", "PATCH"].includes(upperMethod);
  
  let resolvedChangeReason = changeReason || headers["X-Change-Reason"] || headers["x-change-reason"];
  if (!resolvedChangeReason && changeReasonProvider) {
    try {
      resolvedChangeReason = changeReasonProvider();
    } catch (e) {
      // ignore
    }
  }

  if (isMutation && resolvedChangeReason) {
    requestHeaders["X-Change-Reason"] = resolvedChangeReason;
  }

  const baseUrl = apiConfig.getBaseUrl();
  const cleanPath = path.startsWith("/") ? path : \`/\${path}\`;
  const url = \`\${baseUrl}\${cleanPath}\`;

  const fetchOptions = {
    method: upperMethod,
    headers: requestHeaders,
    ...customOptions,
  };

  if (body !== undefined && body !== null) {
    fetchOptions.body = isFormData ? body : (typeof body === "string" ? body : JSON.stringify(body));
  }

  try {
    const response = await fetch(url, fetchOptions);
    if (!response.ok) {
      let data = null;
      try {
        data = await response.json();
      } catch {
        // Not JSON
      }
      throw new ApiError(
        data?.detail ||
          data?.message ||
          \`Request failed with status \${response.status}\`,
        response.status,
        response.statusText,
        data
      );
    }

    if (response.status === 204) {
      return null;
    }
    return await response.json();
  } catch (error) {
    if (error instanceof ApiError) {
      throw error;
    }
    throw new ApiError(error.message || "Network or unknown error occurred");
  }
}

export const apiClient = {
  get(path, options = {}) {
    return request(path, { ...options, method: "GET" });
  },
  post(path, body, options = {}) {
    return request(path, { ...options, method: "POST", body });
  },
  put(path, body, options = {}) {
    return request(path, { ...options, method: "PUT", body });
  },
  patch(path, body, options = {}) {
    return request(path, { ...options, method: "PATCH", body });
  },
  delete(path, options = {}) {
    return request(path, { ...options, method: "DELETE" });
  },
};

export const designerService = {
  getStudy(studyId, options = {}) {
    return apiClient.get(\`/api/v1/studies/\${studyId}\`, options);
  },
  createStudyVersion(studyId, body, options = {}) {
    return apiClient.post(\`/api/v1/studies/\${studyId}/versions\`, body, options);
  },
  getRules(studyId, options = {}) {
    return apiClient.get(\`/api/v1/studies/\${studyId}/rules\`, options);
  },
  createRule(studyId, rule, options = {}) {
    return apiClient.post(\`/api/v1/studies/\${studyId}/rules\`, rule, options);
  },
  getConcepts(options = {}) {
    return apiClient.get(\`/api/v1/mdr/concepts\`, options);
  },
};

export const executionService = {
  createSubject(body, options = {}) {
    return apiClient.post(\`/api/v1/execution/subjects\`, body, options);
  },
  getQueries(options = {}) {
    return apiClient.get(\`/api/v1/execution/queries\`, options);
  },
  listLabAlerts(params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.subject_id) query.append("subject_id", params.subject_id);
    if (params.test_code) query.append("test_code", params.test_code);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/execution/lab-alerts?\${queryString}\`
      : "/api/v1/execution/lab-alerts";
    return apiClient.get(path, options);
  },
  getQuery(queryId, options = {}) {
    return apiClient.get(\`/api/v1/execution/queries/\${queryId}\`, options);
  },
  submitForm(body, options = {}) {
    return apiClient.post(\`/api/v1/execution/form-submissions\`, body, options);
  },
  syncQueries(blocks, options = {}) {
    return apiClient.post(\`/api/v1/execution/queries/sync\`, { blocks }, options);
  },
};

export const etmfService = {
  getDocuments(options = {}) {
    const { status, limit, offset, ...rest } = options;
    const params = new URLSearchParams();
    if (status) params.append("status", status);
    if (limit !== undefined) params.append("limit", limit);
    if (offset !== undefined) params.append("offset", offset);
    const queryString = params.toString();
    const path = queryString
      ? \`/api/v1/etmf/documents?\${queryString}\`
      : "/api/v1/etmf/documents";
    return apiClient.get(path, rest);
  },
  getDocument(documentId, options = {}) {
    return apiClient.get(\`/api/v1/etmf/documents/\${documentId}\`, options);
  },
  ingestDocument(body, options = {}) {
    return apiClient.post(\`/api/v1/etmf/ingest\`, body, options);
  },
  getCompleteness(options = {}) {
    const { study_id, site_id, ...rest } = options;
    const params = new URLSearchParams();
    if (study_id) params.append("study_id", study_id);
    if (site_id) params.append("site_id", site_id);
    const queryString = params.toString();
    const path = queryString
      ? \`/api/v1/etmf/completeness?\${queryString}\`
      : "/api/v1/etmf/completeness";
    return apiClient.get(path, rest);
  },
  verifySignature(payload, options = {}) {
    return apiClient.post("/api/v1/auth/signature-verification", payload, options);
  },
  signOff(documentId, payload, options = {}) {
    return apiClient.post(\`/api/v1/etmf/documents/\${documentId}/sign-off\`, payload, options);
  },
  getArchivalStatus(correlationId, options = {}) {
    return apiClient.get(\`/api/v1/econsent/archival-status/\${correlationId}\`, options);
  },
  getArchivalStatuses(options = {}) {
    const { limit, offset, ...rest } = options;
    const params = new URLSearchParams();
    if (limit !== undefined) params.append("limit", limit);
    if (offset !== undefined) params.append("offset", offset);
    const queryString = params.toString();
    const path = queryString
      ? \`/api/v1/econsent/archival-status?\${queryString}\`
      : "/api/v1/econsent/archival-status";
    return apiClient.get(path, rest);
  },
  getTaxonomy(version, options = {}) {
    const path = version ? \`/api/v1/etmf/taxonomy?version=\${version}\` : "/api/v1/etmf/taxonomy";
    return apiClient.get(path, options);
  },
  autoFile(payload, options = {}) {
    return apiClient.post("/api/v1/etmf/classify", payload, options);
  },
  tagDocument(documentId, payload, options = {}) {
    return apiClient.post(\`/api/v1/etmf/documents/\${documentId}/classify\`, payload, options);
  },
};

export const interopService = {
  submitEpro(body, options = {}) {
    return apiClient.post(\`/api/v1/interop/epro/submit\`, body, options);
  },
  syncEpro(body, options = {}) {
    return apiClient.post(\`/api/v1/interop/epro/sync\`, body, options);
  },
  getInstruments(subjectId, options = {}) {
    return apiClient.get(\`/api/v1/interop/subjects/\${subjectId}/instruments\`, options);
  },
};

export const notificationsService = {
  getNotifications(options = {}) {
    const { category, priority, status, ...rest } = options;
    const params = new URLSearchParams();
    if (category) params.append("category", category);
    if (priority) params.append("priority", priority);
    if (status) params.append("status", status);

    const queryString = params.toString();
    const path = queryString
      ? \`/api/v1/notifications?\${queryString}\`
      : "/api/v1/notifications";
    return apiClient.get(path, rest);
  },
  getNotification(id, options = {}) {
    return apiClient.get(\`/api/v1/notifications/\${id}\`, options);
  },
  acknowledgeNotification(id, options = {}) {
    return apiClient.post(
      \`/api/v1/notifications/\${id}/acknowledge\`,
      {},
      options
    );
  },
  resolveNotification(id, options = {}) {
    return apiClient.post(\`/api/v1/notifications/\${id}/resolve\`, {}, options);
  },
};

export const econsentService = {
  createClause(body, options = {}) {
    return apiClient.post("/api/v1/econsent/clauses", body, options);
  },
  updateClause(clauseId, body, options = {}) {
    return apiClient.put(\`/api/v1/econsent/clauses/\${clauseId}\`, body, options);
  },
  listClauses(params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.clause_id) query.append("clause_id", params.clause_id);
    if (params.all_versions) query.append("all_versions", params.all_versions);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/clauses?\${queryString}\`
      : "/api/v1/econsent/clauses";
    return apiClient.get(path, options);
  },
  getClause(clauseId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/clauses/\${clauseId}?\${queryString}\`
      : \`/api/v1/econsent/clauses/\${clauseId}\`;
    return apiClient.get(path, options);
  },
  createTemplate(body, options = {}) {
    return apiClient.post("/api/v1/econsent/templates", body, options);
  },
  updateTemplate(templateId, body, options = {}) {
    return apiClient.put(
      \`/api/v1/econsent/templates/\${templateId}\`,
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
      ? \`/api/v1/econsent/templates?\${queryString}\`
      : "/api/v1/econsent/templates";
    return apiClient.get(path, options);
  },
  getTemplate(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/templates/\${templateId}?\${queryString}\`
      : \`/api/v1/econsent/templates/\${templateId}\`;
    return apiClient.get(path, options);
  },
  composeTemplate(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/templates/\${templateId}/compose?\${queryString}\`
      : \`/api/v1/econsent/templates/\${templateId}/compose\`;
    return apiClient.get(path, options);
  },
  publishTemplate(templateId, options = {}) {
    return apiClient.post(
      \`/api/v1/econsent/templates/\${templateId}/publish\`,
      {},
      options
    );
  },
  defineComprehensionCheck(templateId, versionIndex, body, options = {}) {
    return apiClient.post(
      \`/api/v1/econsent/templates/\${templateId}/versions/\${versionIndex}/comprehension-checks\`,
      body,
      options
    );
  },
  getComprehensionCheck(templateId, versionIndex, options = {}) {
    return apiClient.get(
      \`/api/v1/econsent/templates/\${templateId}/versions/\${versionIndex}/comprehension-checks\`,
      options
    );
  },
  createTranslation(body, options = {}) {
    return apiClient.post("/api/v1/econsent/translations", body, options);
  },
  updateTranslation(translationId, body, options = {}) {
    return apiClient.put(
      \`/api/v1/econsent/translations/\${translationId}\`,
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
      ? \`/api/v1/econsent/translations?\${queryString}\`
      : "/api/v1/econsent/translations";
    return apiClient.get(path, options);
  },
  getTranslation(translationId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/translations/\${translationId}?\${queryString}\`
      : \`/api/v1/econsent/translations/\${translationId}\`;
    return apiClient.get(path, options);
  },
  transitionTranslation(translationId, body, options = {}) {
    return apiClient.post(
      \`/api/v1/econsent/translations/\${translationId}/transition\`,
      body,
      options
    );
  },
  getApprovedContent(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.language_code)
      query.append("language_code", params.language_code);
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/templates/\${templateId}/approved-content?\${queryString}\`
      : \`/api/v1/econsent/templates/\${templateId}/approved-content\`;
    return apiClient.get(path, options);
  },
};

export const auditorService = {
  getAuditLogs(params = {}) {
    const query = new URLSearchParams();
    if (params.user_id) query.append("user_id", params.user_id);
    if (params.action) query.append("action", params.action);
    if (params.document_id) query.append("document_id", params.document_id);
    if (params.start_time) query.append("start_time", params.start_time);
    if (params.end_time) query.append("end_time", params.end_time);
    if (params.limit !== undefined) query.append("limit", params.limit);
    if (params.offset !== undefined) query.append("offset", params.offset);

    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/etmf/audit-logs?\${queryString}\`
      : "/api/v1/etmf/audit-logs";
    return apiClient.get(path);
  },
  getExecutionIntegrity() {
    return apiClient.get("/api/v1/execution/audit/integrity");
  },
  getWatermarkedDownloadUrl(documentId) {
    const baseUrl = apiConfig.getBaseUrl();
    return \`\${baseUrl}/api/v1/etmf/documents/\${documentId}/watermark\`;
  },
  getBinderExportUrl(studyId, includeHistory = false) {
    const baseUrl = apiConfig.getBaseUrl();
    return \`\${baseUrl}/api/v1/etmf/studies/\${studyId}/binder?include_history=\${includeHistory}\`;
  },
};

export const ingestionClient = {
  uploadProtocol(file, options = {}) {
    const { changeReason = "Upload protocol document", ...rest } = options;
    const formData = new FormData();
    formData.append("file", file);
    return apiClient.post(\`/api/v1/designer/ingestion/upload\`, formData, {
      changeReason,
      ...rest,
    });
  },
  getJobStatus(jobId, options = {}) {
    const { changeReason = "Get job status" } = options;
    return apiClient.get(\`/api/v1/designer/ingestion/jobs/\${jobId}\`, {
      changeReason,
    });
  },
  getCandidate(candidateId, options = {}) {
    const { changeReason = "Get candidate draft" } = options;
    return apiClient.get(
      \`/api/v1/designer/ingestion/candidates/\${candidateId}\`,
      { changeReason }
    );
  },
  transitionItem(
    candidateId,
    itemId,
    status,
    reason,
    updatedFields = {},
    options = {}
  ) {
    const { changeReason = "Review candidate item" } = options;
    return apiClient.post(
      \`/api/v1/designer/ingestion/candidates/\${candidateId}/items/\${itemId}/transition\`,
      { status, reason, ...updatedFields },
      { changeReason }
    );
  },
  promoteCandidate(candidateId, changeReason) {
    return apiClient.post(
      \`/api/v1/designer/ingestion/candidates/\${candidateId}/promote\`,
      { change_reason: changeReason },
      { changeReason }
    );
  },
};

export const soaClient = {
  async getSignedHeaders(changeReason = "") {
    const timestamp = String(Math.floor(Date.now() / 1000));
    const gen = apiConfig.getSignatureGenerator();
    const signature = gen
      ? await gen("usr_dm_fderuiter", "data_manager", timestamp, "2", changeReason, "internal-gateway-secret-12345")
      : "";
    return {
      "X-User-Id": "usr_dm_fderuiter",
      "X-User-Roles": "data_manager",
      "X-Gateway-Timestamp": timestamp,
      "X-Gateway-Signature": signature,
      "X-Signature-Version": "2",
    };
  },
  async getSoAProjection(studyId, versionId, options = {}) {
    const { changeReason = "Get projection" } = options;
    const signedHeaders = await this.getSignedHeaders(changeReason);
    return apiClient.get(
      \`/api/v1/studies/\${studyId}/versions/\${versionId}/soa-projection\`,
      {
        changeReason,
        headers: { ...signedHeaders },
      }
    );
  },
  async mutateEntity(
    studyId,
    versionId,
    entityType,
    entityId,
    properties,
    options = {}
  ) {
    const { changeReason, method = "POST" } = options;
    const isPut = method.toUpperCase() === "PUT";
    const path = \`/api/v1/studies/\${studyId}/versions/\${versionId}/\${entityType}\${isPut ? \`/\${entityId}\` : ""}\`;
    const body = isPut ? { properties } : { id: entityId, properties };

    if (isPut) {
      return apiClient.put(path, body, { changeReason });
    } else {
      return apiClient.post(path, body, { changeReason });
    }
  },
  async saveArm(studyId, versionId, armId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "arms",
      armId,
      properties,
      options
    );
  },
  async saveEpoch(studyId, versionId, epochId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "epochs",
      epochId,
      properties,
      options
    );
  },
  async saveVisit(studyId, versionId, visitId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "visits",
      visitId,
      properties,
      options
    );
  },
  async saveProcedure(studyId, versionId, procedureId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "procedures",
      procedureId,
      properties,
      options
    );
  },
  async createLink(studyId, versionId, linkType, payload, options = {}) {
    const { changeReason } = options;
    return apiClient.post(
      \`/api/v1/studies/\${studyId}/versions/\${versionId}/links/\${linkType}\`,
      payload,
      { changeReason }
    );
  },
  async verifySignature({
    username,
    password,
    totp = null,
    action,
    batchId = null,
  }) {
    return apiClient.post("/api/v1/auth/signature-verification", {
      username,
      password,
      totp,
      action,
      batch_id: batchId,
    });
  },
  async batchSignOff(
    { studyId, targetType, targetIds, signingReason },
    { changeReason, sigToken }
  ) {
    return apiClient.post(
      "/api/v1/execution/batch-sign-off",
      {
        study_id: studyId,
        target_type: targetType,
        target_ids: targetIds,
        signing_reason: signingReason,
      },
      {
        changeReason,
        headers: {
          "X-Sig-Token": sigToken,
        },
      }
    );
  },
};

export const terminologyClient = {
  async validateSingleCode(code, options = {}) {
    if (!code || !code.trim()) {
      throw new Error("Concept code cannot be empty or whitespace.");
    }
    const changeReason = options.changeReason || "Validate code";
    try {
      return await apiClient.get(
        \`/api/v1/terminology/validate/\${encodeURIComponent(code)}\`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async searchTerminology(term, options = {}) {
    if (!term || !term.trim()) {
      throw new Error("Search term cannot be empty or whitespace.");
    }
    const {
      fromRecord,
      pageSize,
      changeReason = "Search terminology",
    } = options;
    const queryParams = new URLSearchParams({ term });
    if (fromRecord !== undefined && fromRecord !== null) {
      queryParams.append("from_record", String(fromRecord));
    }
    if (pageSize !== undefined && pageSize !== null) {
      queryParams.append("page_size", String(pageSize));
    }
    try {
      return await apiClient.get(
        \`/api/v1/terminology/search?\${queryParams.toString()}\`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async getStudyTerminologyValidation(studyId, options = {}) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }
    const changeReason = options.changeReason || "Get study terminology validation";
    try {
      return await apiClient.get(
        \`/api/v1/studies/\${encodeURIComponent(studyId)}/terminology-validation\`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async getStudyCtValidation(studyId, options = {}) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }
    const changeReason = options.changeReason || "Get study CT validation";
    try {
      return await apiClient.get(
        \`/api/v1/studies/\${encodeURIComponent(studyId)}/ct-validation\`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
};
`;

const dtsContent = `export interface ApiConfig {
  setTokenProvider(fn: () => string | null | undefined): void;
  setChangeReasonProvider(fn: () => string | null | undefined): void;
  setBaseUrlProvider(fn: () => string): void;
  setSignatureGenerator(fn: (...args: any[]) => Promise<string>): void;
  getSignatureGenerator(): ((...args: any[]) => Promise<string>) | null;
  getBaseUrl(): string;
}

export const apiConfig: ApiConfig;

export class ApiError extends Error {
  status: number | null;
  statusText: string | null;
  data: any;
  constructor(message: string, status?: number | null, statusText?: string | null, data?: any);
}

export class TerminologyNetworkError extends Error {
  status: number | null;
  statusText: string | null;
  constructor(message: string, status?: number | null, statusText?: string | null);
}

export interface RequestOptions {
  method?: string;
  headers?: Record<string, string>;
  body?: any;
  changeReason?: string;
  [key: string]: any;
}

export interface ApiClient {
  get(path: string, options?: RequestOptions): Promise<any>;
  post(path: string, body?: any, options?: RequestOptions): Promise<any>;
  put(path: string, body?: any, options?: RequestOptions): Promise<any>;
  patch(path: string, body?: any, options?: RequestOptions): Promise<any>;
  delete(path: string, options?: RequestOptions): Promise<any>;
}

export const apiClient: ApiClient;

export interface DesignerService {
  getStudy(studyId: string, options?: RequestOptions): Promise<any>;
  createStudyVersion(studyId: string, body: any, options?: RequestOptions): Promise<any>;
  getRules(studyId: string, options?: RequestOptions): Promise<any>;
  createRule(studyId: string, rule: any, options?: RequestOptions): Promise<any>;
  getConcepts(options?: RequestOptions): Promise<any>;
}

export const designerService: DesignerService;

export interface ExecutionService {
  createSubject(body: any, options?: RequestOptions): Promise<any>;
  getQueries(options?: RequestOptions): Promise<any>;
  listLabAlerts(params?: any, options?: RequestOptions): Promise<any>;
  getQuery(queryId: string, options?: RequestOptions): Promise<any>;
  submitForm(body: any, options?: RequestOptions): Promise<any>;
  syncQueries(blocks: any[], options?: RequestOptions): Promise<any>;
}

export const executionService: ExecutionService;

export interface EtmfService {
  getDocuments(options?: RequestOptions): Promise<any>;
  getDocument(documentId: string, options?: RequestOptions): Promise<any>;
  ingestDocument(body: any, options?: RequestOptions): Promise<any>;
  getCompleteness(options?: RequestOptions): Promise<any>;
  verifySignature(payload: any, options?: RequestOptions): Promise<any>;
  signOff(documentId: string, payload: any, options?: RequestOptions): Promise<any>;
  getArchivalStatus(correlationId: string, options?: RequestOptions): Promise<any>;
  getArchivalStatuses(options?: RequestOptions): Promise<any>;
  getTaxonomy(version?: string, options?: RequestOptions): Promise<any>;
  autoFile(payload: any, options?: RequestOptions): Promise<any>;
  tagDocument(documentId: string, payload: any, options?: RequestOptions): Promise<any>;
}

export const etmfService: EtmfService;

export interface InteropService {
  submitEpro(body: any, options?: RequestOptions): Promise<any>;
  syncEpro(body: any, options?: RequestOptions): Promise<any>;
  getInstruments(subjectId: string, options?: RequestOptions): Promise<any>;
}

export const interopService: InteropService;

export interface NotificationsService {
  getNotifications(options?: RequestOptions): Promise<any>;
  getNotification(id: string, options?: RequestOptions): Promise<any>;
  acknowledgeNotification(id: string, options?: RequestOptions): Promise<any>;
  resolveNotification(id: string, options?: RequestOptions): Promise<any>;
}

export const notificationsService: NotificationsService;

export interface EconsentService {
  createClause(body: any, options?: RequestOptions): Promise<any>;
  updateClause(clauseId: string, body: any, options?: RequestOptions): Promise<any>;
  listClauses(params?: any, options?: RequestOptions): Promise<any>;
  getClause(clauseId: string, params?: any, options?: RequestOptions): Promise<any>;
  createTemplate(body: any, options?: RequestOptions): Promise<any>;
  updateTemplate(templateId: string, body: any, options?: RequestOptions): Promise<any>;
  listTemplates(params?: any, options?: RequestOptions): Promise<any>;
  getTemplate(templateId: string, params?: any, options?: RequestOptions): Promise<any>;
  composeTemplate(templateId: string, params?: any, options?: RequestOptions): Promise<any>;
  publishTemplate(templateId: string, options?: RequestOptions): Promise<any>;
  defineComprehensionCheck(templateId: string, versionIndex: number, body: any, options?: RequestOptions): Promise<any>;
  getComprehensionCheck(templateId: string, versionIndex: number, options?: RequestOptions): Promise<any>;
  createTranslation(body: any, options?: RequestOptions): Promise<any>;
  updateTranslation(translationId: string, body: any, options?: RequestOptions): Promise<any>;
  listTranslations(params?: any, options?: RequestOptions): Promise<any>;
  getTranslation(translationId: string, params?: any, options?: RequestOptions): Promise<any>;
  transitionTranslation(translationId: string, body: any, options?: RequestOptions): Promise<any>;
  getApprovedContent(templateId: string, params?: any, options?: RequestOptions): Promise<any>;
}

export const econsentService: EconsentService;

export interface AuditorService {
  getAuditLogs(params?: any): Promise<any>;
  getExecutionIntegrity(): Promise<any>;
  getWatermarkedDownloadUrl(documentId: string): string;
  getBinderExportUrl(studyId: string, includeHistory?: boolean): string;
}

export const auditorService: AuditorService;

export interface IngestionClient {
  uploadProtocol(file: any, options?: RequestOptions): Promise<any>;
  getJobStatus(jobId: string, options?: RequestOptions): Promise<any>;
  getCandidate(candidateId: string, options?: RequestOptions): Promise<any>;
  transitionItem(candidateId: string, itemId: string, status: string, reason: string, updatedFields?: any, options?: RequestOptions): Promise<any>;
  promoteCandidate(candidateId: string, changeReason: string): Promise<any>;
}

export const ingestionClient: IngestionClient;

export interface SoaClient {
  getSignedHeaders(changeReason?: string): Promise<Record<string, string>>;
  getSoAProjection(studyId: string, versionId: string, options?: RequestOptions): Promise<any>;
  mutateEntity(studyId: string, versionId: string, entityType: string, entityId: string, properties: any, options?: RequestOptions): Promise<any>;
  saveArm(studyId: string, versionId: string, armId: string, properties: any, options?: RequestOptions): Promise<any>;
  saveEpoch(studyId: string, versionId: string, epochId: string, properties: any, options?: RequestOptions): Promise<any>;
  saveVisit(studyId: string, versionId: string, visitId: string, properties: any, options?: RequestOptions): Promise<any>;
  saveProcedure(studyId: string, versionId: string, procedureId: string, properties: any, options?: RequestOptions): Promise<any>;
  createLink(studyId: string, versionId: string, linkType: string, payload: any, options?: RequestOptions): Promise<any>;
  verifySignature(payload: any): Promise<any>;
  batchSignOff(payload: any, options: { changeReason?: string; sigToken: string }): Promise<any>;
}

export const soaClient: SoaClient;

export interface TerminologyClient {
  validateSingleCode(code: string, options?: RequestOptions): Promise<any>;
  searchTerminology(term: string, options?: RequestOptions): Promise<any>;
  getStudyTerminologyValidation(studyId: string, options?: RequestOptions): Promise<any>;
  getStudyCtValidation(studyId: string, options?: RequestOptions): Promise<any>;
}

export const terminologyClient: TerminologyClient;
`;

fs.writeFileSync(targetJsPath, jsContent);
fs.writeFileSync(targetDtsPath, dtsContent);
console.log('Successfully generated shared-api-client files index.js and index.d.ts!');

// Now, generate/write the re-exporting wrappers in apps/web/src/api
const webApiDir = '/app/apps/web/src/api';

const wrappers = {
  'apiClient.js': `import { apiClient, apiConfig, ApiError } from "shared-api-client";
import { useAuthStore } from "../stores/auth";
import { generateGatewaySignature } from "ui";

apiConfig.setTokenProvider(() => {
  try {
    const authStore = useAuthStore();
    return authStore?.token || authStore?.accessToken;
  } catch {
    return null;
  }
});

apiConfig.setSignatureGenerator(generateGatewaySignature);

export const getBaseUrl = () => apiConfig.getBaseUrl();
export { apiClient, ApiError };
`,
  'designer.js': `import { apiClient } from "./apiClient";

export const designerService = {
  getStudy(studyId, options = {}) {
    return apiClient.get(\`/api/v1/studies/\${studyId}\`, options);
  },
  createStudyVersion(studyId, body, options = {}) {
    return apiClient.post(\`/api/v1/studies/\${studyId}/versions\`, body, options);
  },
  getRules(studyId, options = {}) {
    return apiClient.get(\`/api/v1/studies/\${studyId}/rules\`, options);
  },
  createRule(studyId, rule, options = {}) {
    return apiClient.post(\`/api/v1/studies/\${studyId}/rules\`, rule, options);
  },
  getConcepts(options = {}) {
    return apiClient.get(\`/api/v1/mdr/concepts\`, options);
  },
};
`,
  'execution.js': `import { apiClient } from "./apiClient";

export const executionService = {
  createSubject(body, options = {}) {
    return apiClient.post(\`/api/v1/execution/subjects\`, body, options);
  },
  getQueries(options = {}) {
    return apiClient.get(\`/api/v1/execution/queries\`, options);
  },
  listLabAlerts(params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.subject_id) query.append("subject_id", params.subject_id);
    if (params.test_code) query.append("test_code", params.test_code);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/execution/lab-alerts?\${queryString}\`
      : "/api/v1/execution/lab-alerts";
    return apiClient.get(path, options);
  },
  getQuery(queryId, options = {}) {
    return apiClient.get(\`/api/v1/execution/queries/\${queryId}\`, options);
  },
  submitForm(body, options = {}) {
    return apiClient.post(\`/api/v1/execution/form-submissions\`, body, options);
  },
  syncQueries(blocks, options = {}) {
    return apiClient.post(\`/api/v1/execution/queries/sync\`, { blocks }, options);
  },
};
`,
  'etmf.js': `import { apiClient } from "./apiClient";

export const etmfService = {
  getDocuments(options = {}) {
    const { status, limit, offset, ...rest } = options;
    const params = new URLSearchParams();
    if (status) params.append("status", status);
    if (limit !== undefined) params.append("limit", limit);
    if (offset !== undefined) params.append("offset", offset);
    const queryString = params.toString();
    const path = queryString
      ? \`/api/v1/etmf/documents?\${queryString}\`
      : "/api/v1/etmf/documents";
    return apiClient.get(path, rest);
  },
  getDocument(documentId, options = {}) {
    return apiClient.get(\`/api/v1/etmf/documents/\${documentId}\`, options);
  },
  ingestDocument(body, options = {}) {
    return apiClient.post(\`/api/v1/etmf/ingest\`, body, options);
  },
  getCompleteness(options = {}) {
    const { study_id, site_id, ...rest } = options;
    const params = new URLSearchParams();
    if (study_id) params.append("study_id", study_id);
    if (site_id) params.append("site_id", site_id);
    const queryString = params.toString();
    const path = queryString
      ? \`/api/v1/etmf/completeness?\${queryString}\`
      : "/api/v1/etmf/completeness";
    return apiClient.get(path, rest);
  },
  verifySignature(payload, options = {}) {
    return apiClient.post("/api/v1/auth/signature-verification", payload, options);
  },
  signOff(documentId, payload, options = {}) {
    return apiClient.post(\`/api/v1/etmf/documents/\${documentId}/sign-off\`, payload, options);
  },
  getArchivalStatus(correlationId, options = {}) {
    return apiClient.get(\`/api/v1/econsent/archival-status/\${correlationId}\`, options);
  },
  getArchivalStatuses(options = {}) {
    const { limit, offset, ...rest } = options;
    const params = new URLSearchParams();
    if (limit !== undefined) params.append("limit", limit);
    if (offset !== undefined) params.append("offset", offset);
    const queryString = params.toString();
    const path = queryString
      ? \`/api/v1/econsent/archival-status?\${queryString}\`
      : "/api/v1/econsent/archival-status";
    return apiClient.get(path, rest);
  },
  getTaxonomy(version, options = {}) {
    const path = version ? \`/api/v1/etmf/taxonomy?version=\${version}\` : "/api/v1/etmf/taxonomy";
    return apiClient.get(path, options);
  },
  autoFile(payload, options = {}) {
    return apiClient.post("/api/v1/etmf/classify", payload, options);
  },
  tagDocument(documentId, payload, options = {}) {
    return apiClient.post(\`/api/v1/etmf/documents/\${documentId}/classify\`, payload, options);
  },
};
`,
  'interop.js': `import { apiClient } from "./apiClient";

export const interopService = {
  submitEpro(body, options = {}) {
    return apiClient.post(\`/api/v1/interop/epro/submit\`, body, options);
  },
  syncEpro(body, options = {}) {
    return apiClient.post(\`/api/v1/interop/epro/sync\`, body, options);
  },
  getInstruments(subjectId, options = {}) {
    return apiClient.get(\`/api/v1/interop/subjects/\${subjectId}/instruments\`, options);
  },
};
`,
  'notifications.js': `import { apiClient } from "./apiClient";

export const notificationsService = {
  getNotifications(options = {}) {
    const { category, priority, status, ...rest } = options;
    const params = new URLSearchParams();
    if (category) params.append("category", category);
    if (priority) params.append("priority", priority);
    if (status) params.append("status", status);

    const queryString = params.toString();
    const path = queryString
      ? \`/api/v1/notifications?\${queryString}\`
      : "/api/v1/notifications";
    return apiClient.get(path, rest);
  },
  getNotification(id, options = {}) {
    return apiClient.get(\`/api/v1/notifications/\${id}\`, options);
  },
  acknowledgeNotification(id, options = {}) {
    return apiClient.post(
      \`/api/v1/notifications/\${id}/acknowledge\`,
      {},
      options
    );
  },
  resolveNotification(id, options = {}) {
    return apiClient.post(\`/api/v1/notifications/\${id}/resolve\`, {}, options);
  },
};
`,
  'econsent.js': `import { apiClient } from "./apiClient";

export const econsentService = {
  createClause(body, options = {}) {
    return apiClient.post("/api/v1/econsent/clauses", body, options);
  },
  updateClause(clauseId, body, options = {}) {
    return apiClient.put(\`/api/v1/econsent/clauses/\${clauseId}\`, body, options);
  },
  listClauses(params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.study_id) query.append("study_id", params.study_id);
    if (params.clause_id) query.append("clause_id", params.clause_id);
    if (params.all_versions) query.append("all_versions", params.all_versions);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/clauses?\${queryString}\`
      : "/api/v1/econsent/clauses";
    return apiClient.get(path, options);
  },
  getClause(clauseId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/clauses/\${clauseId}?\${queryString}\`
      : \`/api/v1/econsent/clauses/\${clauseId}\`;
    return apiClient.get(path, options);
  },
  createTemplate(body, options = {}) {
    return apiClient.post("/api/v1/econsent/templates", body, options);
  },
  updateTemplate(templateId, body, options = {}) {
    return apiClient.put(
      \`/api/v1/econsent/templates/\${templateId}\`,
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
      ? \`/api/v1/econsent/templates?\${queryString}\`
      : "/api/v1/econsent/templates";
    return apiClient.get(path, options);
  },
  getTemplate(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/templates/\${templateId}?\${queryString}\`
      : \`/api/v1/econsent/templates/\${templateId}\`;
    return apiClient.get(path, options);
  },
  composeTemplate(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/templates/\${templateId}/compose?\${queryString}\`
      : \`/api/v1/econsent/templates/\${templateId}/compose\`;
    return apiClient.get(path, options);
  },
  publishTemplate(templateId, options = {}) {
    return apiClient.post(
      \`/api/v1/econsent/templates/\${templateId}/publish\`,
      {},
      options
    );
  },
  defineComprehensionCheck(templateId, versionIndex, body, options = {}) {
    return apiClient.post(
      \`/api/v1/econsent/templates/\${templateId}/versions/\${versionIndex}/comprehension-checks\`,
      body,
      options
    );
  },
  getComprehensionCheck(templateId, versionIndex, options = {}) {
    return apiClient.get(
      \`/api/v1/econsent/templates/\${templateId}/versions/\${versionIndex}/comprehension-checks\`,
      options
    );
  },
  createTranslation(body, options = {}) {
    return apiClient.post("/api/v1/econsent/translations", body, options);
  },
  updateTranslation(translationId, body, options = {}) {
    return apiClient.put(
      \`/api/v1/econsent/translations/\${translationId}\`,
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
      ? \`/api/v1/econsent/translations?\${queryString}\`
      : "/api/v1/econsent/translations";
    return apiClient.get(path, options);
  },
  getTranslation(translationId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/translations/\${translationId}?\${queryString}\`
      : \`/api/v1/econsent/translations/\${translationId}\`;
    return apiClient.get(path, options);
  },
  transitionTranslation(translationId, body, options = {}) {
    return apiClient.post(
      \`/api/v1/econsent/translations/\${translationId}/transition\`,
      body,
      options
    );
  },
  getApprovedContent(templateId, params = {}, options = {}) {
    const query = new URLSearchParams();
    if (params.language_code)
      query.append("language_code", params.language_code);
    if (params.version_index !== undefined)
      query.append("version_index", params.version_index);
    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/econsent/templates/\${templateId}/approved-content?\${queryString}\`
      : \`/api/v1/econsent/templates/\${templateId}/approved-content\`;
    return apiClient.get(path, options);
  },
};
`,
  'auditor.js': `import { apiClient, getBaseUrl } from "./apiClient";

export const auditorService = {
  getAuditLogs(params = {}) {
    const query = new URLSearchParams();
    if (params.user_id) query.append("user_id", params.user_id);
    if (params.action) query.append("action", params.action);
    if (params.document_id) query.append("document_id", params.document_id);
    if (params.start_time) query.append("start_time", params.start_time);
    if (params.end_time) query.append("end_time", params.end_time);
    if (params.limit !== undefined) query.append("limit", params.limit);
    if (params.offset !== undefined) query.append("offset", params.offset);

    const queryString = query.toString();
    const path = queryString
      ? \`/api/v1/etmf/audit-logs?\${queryString}\`
      : "/api/v1/etmf/audit-logs";
    return apiClient.get(path);
  },
  getExecutionIntegrity() {
    return apiClient.get("/api/v1/execution/audit/integrity");
  },
  getWatermarkedDownloadUrl(documentId) {
    const baseUrl = getBaseUrl();
    return \`\${baseUrl}/api/v1/etmf/documents/\${documentId}/watermark\`;
  },
  getBinderExportUrl(studyId, includeHistory = false) {
    const baseUrl = getBaseUrl();
    return \`\${baseUrl}/api/v1/etmf/studies/\${studyId}/binder?include_history=\${includeHistory}\`;
  },
};
`,
  'ingestionClient.js': `import { apiClient } from "./apiClient";

export const ingestionClient = {
  uploadProtocol(file, options = {}) {
    const { changeReason = "Upload protocol document", ...rest } = options;
    const formData = new FormData();
    formData.append("file", file);
    return apiClient.post(\`/api/v1/designer/ingestion/upload\`, formData, {
      changeReason,
      ...rest,
    });
  },
  getJobStatus(jobId, options = {}) {
    const { changeReason = "Get job status" } = options;
    return apiClient.get(\`/api/v1/designer/ingestion/jobs/\${jobId}\`, {
      changeReason,
    });
  },
  getCandidate(candidateId, options = {}) {
    const { changeReason = "Get candidate draft" } = options;
    return apiClient.get(
      \`/api/v1/designer/ingestion/candidates/\${candidateId}\`,
      { changeReason }
    );
  },
  transitionItem(
    candidateId,
    itemId,
    status,
    reason,
    updatedFields = {},
    options = {}
  ) {
    const { changeReason = "Review candidate item" } = options;
    return apiClient.post(
      \`/api/v1/designer/ingestion/candidates/\${candidateId}/items/\${itemId}/transition\`,
      { status, reason, ...updatedFields },
      { changeReason }
    );
  },
  promoteCandidate(candidateId, changeReason) {
    return apiClient.post(
      \`/api/v1/designer/ingestion/candidates/\${candidateId}/promote\`,
      { change_reason: changeReason },
      { changeReason }
    );
  },
};
`,
  'soaClient.js': `import { apiClient } from "./apiClient";
import { generateGatewaySignature } from "ui";

export const soaClient = {
  async getSignedHeaders(changeReason = "") {
    const timestamp = String(Math.floor(Date.now() / 1000));
    const signature = await generateGatewaySignature(
      "usr_dm_fderuiter",
      "data_manager",
      timestamp,
      "2",
      changeReason,
      "internal-gateway-secret-12345"
    );
    return {
      "X-User-Id": "usr_dm_fderuiter",
      "X-User-Roles": "data_manager",
      "X-Gateway-Timestamp": timestamp,
      "X-Gateway-Signature": signature,
      "X-Signature-Version": "2",
    };
  },
  async getSoAProjection(studyId, versionId, options = {}) {
    const { changeReason = "Get projection" } = options;
    const signedHeaders = await this.getSignedHeaders(changeReason);
    return apiClient.get(
      \`/api/v1/studies/\${studyId}/versions/\${versionId}/soa-projection\`,
      {
        changeReason,
        headers: { ...signedHeaders },
      }
    );
  },
  async mutateEntity(
    studyId,
    versionId,
    entityType,
    entityId,
    properties,
    options = {}
  ) {
    const { changeReason, method = "POST" } = options;
    const isPut = method.toUpperCase() === "PUT";
    const path = \`/api/v1/studies/\${studyId}/versions/\${versionId}/\${entityType}\${isPut ? \`/\${entityId}\` : ""}\`;
    const body = isPut ? { properties } : { id: entityId, properties };

    if (isPut) {
      return apiClient.put(path, body, { changeReason });
    } else {
      return apiClient.post(path, body, { changeReason });
    }
  },
  async saveArm(studyId, versionId, armId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "arms",
      armId,
      properties,
      options
    );
  },
  async saveEpoch(studyId, versionId, epochId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "epochs",
      epochId,
      properties,
      options
    );
  },
  async saveVisit(studyId, versionId, visitId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "visits",
      visitId,
      properties,
      options
    );
  },
  async saveProcedure(studyId, versionId, procedureId, properties, options) {
    return this.mutateEntity(
      studyId,
      versionId,
      "procedures",
      procedureId,
      properties,
      options
    );
  },
  async createLink(studyId, versionId, linkType, payload, options = {}) {
    const { changeReason } = options;
    return apiClient.post(
      \`/api/v1/studies/\${studyId}/versions/\${versionId}/links/\${linkType}\`,
      payload,
      { changeReason }
    );
  },
  async verifySignature({
    username,
    password,
    totp = null,
    action,
    batchId = null,
  }) {
    return apiClient.post("/api/v1/auth/signature-verification", {
      username,
      password,
      totp,
      action,
      batch_id: batchId,
    });
  },
  async batchSignOff(
    { studyId, targetType, targetIds, signingReason },
    { changeReason, sigToken }
  ) {
    return apiClient.post(
      "/api/v1/execution/batch-sign-off",
      {
        study_id: studyId,
        target_type: targetType,
        target_ids: targetIds,
        signing_reason: signingReason,
      },
      {
        changeReason,
        headers: {
          "X-Sig-Token": sigToken,
        },
      }
    );
  },
};
`,
  'terminologyClient.js': `import { apiClient } from "./apiClient";
import { TerminologyNetworkError } from "shared-api-client";

export { TerminologyNetworkError };

export const terminologyClient = {
  async validateSingleCode(code, options = {}) {
    if (!code || !code.trim()) {
      throw new Error("Concept code cannot be empty or whitespace.");
    }
    const changeReason = options.changeReason || "Validate code";
    try {
      return await apiClient.get(
        \`/api/v1/terminology/validate/\${encodeURIComponent(code)}\`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async searchTerminology(term, options = {}) {
    if (!term || !term.trim()) {
      throw new Error("Search term cannot be empty or whitespace.");
    }
    const {
      fromRecord,
      pageSize,
      changeReason = "Search terminology",
    } = options;
    const queryParams = new URLSearchParams({ term });
    if (fromRecord !== undefined && fromRecord !== null) {
      queryParams.append("from_record", String(fromRecord));
    }
    if (pageSize !== undefined && pageSize !== null) {
      queryParams.append("page_size", String(pageSize));
    }
    try {
      return await apiClient.get(
        \`/api/v1/terminology/search?\${queryParams.toString()}\`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async getStudyTerminologyValidation(studyId, options = {}) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }
    const changeReason = options.changeReason || "Get study terminology validation";
    try {
      return await apiClient.get(
        \`/api/v1/studies/\${encodeURIComponent(studyId)}/terminology-validation\`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
  async getStudyCtValidation(studyId, options = {}) {
    if (!studyId) {
      throw new Error("Study ID cannot be empty.");
    }
    const changeReason = options.changeReason || "Get study CT validation";
    try {
      return await apiClient.get(
        \`/api/v1/studies/\${encodeURIComponent(studyId)}/ct-validation\`,
        { changeReason }
      );
    } catch (error) {
      throw new TerminologyNetworkError(
        error.message || "Network error occurred",
        error.status,
        error.statusText
      );
    }
  },
};
`
};

for (const [filename, content] of Object.entries(wrappers)) {
  const filePath = path.join(webApiDir, filename);
  fs.writeFileSync(filePath, content);
  console.log("Successfully generated re-export wrapper: " + filePath);
}
