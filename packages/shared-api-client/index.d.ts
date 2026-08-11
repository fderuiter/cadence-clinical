export interface ApiConfig {
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
